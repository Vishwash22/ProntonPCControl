using System.Runtime.InteropServices;
using static FocusLock.NativeMethods;

namespace FocusLock;

/// <param name="Window">The window the parent picked; it is maximized and kept in front.</param>
/// <param name="ProcessName">Every window of this process stays usable (Zoom's chat, dialogs, share toolbar, ...).</param>
/// <param name="Duration">How long the lock lasts, or null to last until a parent unlocks.</param>
/// <param name="WatchOnly">Block all typing and clicking too, so the child can only watch.</param>
internal sealed record LockOptions(IntPtr Window, string ProcessName, TimeSpan? Duration, EndAction EndAction, bool WatchOnly);

/// <summary>
/// Keeps the child inside one app: re-focuses and un-minimizes it four times a second, swallows
/// app-switching and closing shortcuts, ignores clicks outside the app and on its Minimize/Close
/// buttons, and covers the screen if the app goes away. Ends on time-out or with the parent PIN.
/// </summary>
internal sealed class LockSession
{
    private readonly LockOptions options;
    private readonly Settings settings;
    private readonly ProcessNames processNames = new();
    private readonly uint ownPid = (uint)Environment.ProcessId;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 250 };
    private readonly DateTime? endsAt;

    // Hook delegates must stay referenced, or the garbage collector frees them while Windows still calls them.
    private readonly LowLevelHookProc keyboardProc;
    private readonly LowLevelHookProc mouseProc;
    private IntPtr keyboardHook;
    private IntPtr mouseHook;

    private SynchronizationContext? uiContext;
    private CountdownForm? countdown;
    private BlockerForm? blocker;
    private IntPtr lockedWindow;
    private bool timeUp;
    private bool ended;
    private bool unlockDialogOpen;
    private readonly HashSet<uint> heldModifiers = new();
    private int wrongPins;
    private DateTime pinLockedUntil;

    public LockSession(LockOptions options, Settings settings)
    {
        this.options = options;
        this.settings = settings;
        endsAt = DateTime.UtcNow + options.Duration;
        keyboardProc = KeyboardProc;
        mouseProc = MouseProc;
    }

    /// <summary>Raised once with a message for the parent when the lock is lifted.</summary>
    public event Action<string>? Ended;

    /// <summary>Starts the lock; returns warnings about restrictions that could not be applied.</summary>
    public List<string> Start()
    {
        uiContext = SynchronizationContext.Current;
        var warnings = SystemRestrictions.Apply();

        IntPtr module = GetModuleHandle(null);
        keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, keyboardProc, module, 0);
        mouseHook = SetWindowsHookEx(WH_MOUSE_LL, mouseProc, module, 0);
        if (keyboardHook == IntPtr.Zero || mouseHook == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            Stop();
            throw new InvalidOperationException($"Could not install the keyboard and mouse hooks (error {error}).");
        }

        lockedWindow = options.Window;
        if (IsWindow(lockedWindow))
        {
            ShowWindow(lockedWindow, IsIconic(lockedWindow) ? SW_RESTORE : SW_SHOW);
            ShowWindow(lockedWindow, SW_MAXIMIZE);
            WindowFinder.ForceForeground(lockedWindow);
        }

        countdown = new CountdownForm();
        countdown.Show();
        timer.Tick += (_, _) => Tick();
        timer.Start();
        Tick();
        return warnings;
    }

    /// <summary>Shows the PIN prompt; unlocks if the parent enters the right PIN.</summary>
    public void RequestUnlock()
    {
        if (ended || unlockDialogOpen)
        {
            return;
        }

        unlockDialogOpen = true;
        try
        {
            using var dialog = new PinForm("Enter the parent PIN to unlock:", CheckPin);
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                Finish("Unlocked by a parent.");
            }
        }
        finally
        {
            unlockDialogOpen = false;
        }
    }

    private string? CheckPin(string pin)
    {
        if (DateTime.UtcNow < pinLockedUntil)
        {
            return $"Too many wrong tries. Wait {(int)Math.Ceiling((pinLockedUntil - DateTime.UtcNow).TotalSeconds)} seconds.";
        }
        if (settings.CheckPin(pin))
        {
            return null;
        }

        wrongPins++;
        if (wrongPins % 3 == 0)
        {
            // Every third wrong PIN locks the prompt for longer, so guessing takes forever.
            pinLockedUntil = DateTime.UtcNow.AddSeconds(30 * (wrongPins / 3));
            return "Wrong PIN. Too many wrong tries, please wait.";
        }
        return "Wrong PIN.";
    }

    private void Tick()
    {
        if (ended)
        {
            return;
        }

        TimeSpan? remaining = endsAt - DateTime.UtcNow;
        if (!timeUp && remaining <= TimeSpan.Zero)
        {
            timeUp = true;
            if (options.EndAction == EndAction.Unlock)
            {
                Finish("Time is up, so the lock was lifted.");
                return;
            }
            if (options.EndAction == EndAction.LockWindows)
            {
                Finish("Time is up, so Windows was locked.");
                LockWorkStation();
                return;
            }
        }

        SystemRestrictions.HideTaskbars();
        lockedWindow = timeUp ? IntPtr.Zero : FindLockedWindow();
        if (lockedWindow == IntPtr.Zero)
        {
            ShowBlocker(timeUp ? "Time's up!" : $"{options.ProcessName} was closed.\nAsk a parent to unlock the computer.");
        }
        else
        {
            HideBlocker();
            if (IsIconic(lockedWindow))
            {
                ShowWindow(lockedWindow, SW_RESTORE);
            }
            if (!IsAllowedWindow(GetForegroundWindow()))
            {
                WindowFinder.ForceForeground(lockedWindow);
            }
        }

        string mode = options.WatchOnly ? "Watch only" : "Locked to";
        countdown?.SetMessage(timeUp || remaining == null
            ? $"{mode}: {options.ProcessName} until a parent unlocks"
            : $"{mode}: {options.ProcessName} · {FormatRemaining(remaining.Value)} left");
    }

    private static string FormatRemaining(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t:mm\\:ss}" : t.ToString("mm\\:ss");

    /// <summary>The picked window while it exists; otherwise another window of the same app (Zoom swaps windows when a meeting starts).</summary>
    private IntPtr FindLockedWindow()
    {
        if (IsWindow(lockedWindow) && IsWindowVisible(lockedWindow) && IsLockedApp(WindowFinder.GetProcessId(lockedWindow)))
        {
            return lockedWindow;
        }
        return WindowFinder.FindMainWindow(options.ProcessName, processNames);
    }

    private bool IsLockedApp(uint pid) =>
        !timeUp && string.Equals(processNames.Get(pid), options.ProcessName, StringComparison.OrdinalIgnoreCase);

    private bool IsAllowedWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }
        uint pid = WindowFinder.GetProcessId(hwnd);
        return pid == ownPid || IsLockedApp(pid);
    }

    private void ShowBlocker(string message)
    {
        if (blocker == null)
        {
            blocker = new BlockerForm();
            blocker.UnlockClicked += (_, _) => RequestUnlock();
            blocker.Show();
        }
        blocker.SetMessage(message);
        if (!IsAllowedWindow(GetForegroundWindow()))
        {
            WindowFinder.ForceForeground(blocker.Handle);
        }
    }

    private void HideBlocker()
    {
        blocker?.CloseForGood();
        blocker = null;
    }

    private void Finish(string message)
    {
        if (ended)
        {
            return;
        }
        Stop();
        Ended?.Invoke(message);
    }

    private void Stop()
    {
        ended = true;
        timer.Stop();
        timer.Dispose();
        if (keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(keyboardHook);
        }
        if (mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(mouseHook);
        }
        countdown?.Close();
        HideBlocker();
        SystemRestrictions.Restore();
    }

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var key = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (key.dwExtraInfo != InjectedByFocusLock)
            {
                int message = (int)wParam;
                bool keyDown = message is WM_KEYDOWN or WM_SYSKEYDOWN;
                TrackModifier(key.vkCode, keyDown);
                var verdict = KeyRules.Evaluate(
                    key.vkCode,
                    keyDown,
                    alt: (key.flags & LLKHF_ALTDOWN) != 0 || IsHeld(Keys.LMenu, Keys.RMenu, Keys.Menu),
                    ctrl: IsHeld(Keys.LControlKey, Keys.RControlKey, Keys.ControlKey),
                    shift: IsHeld(Keys.LShiftKey, Keys.RShiftKey, Keys.ShiftKey),
                    // The PIN prompt is FocusLock's own window, so the parent can type there.
                    blockAll: options.WatchOnly && !unlockDialogOpen);
                if (verdict == KeyVerdict.ParentUnlock)
                {
                    // Never open a dialog inside the hook; Windows drops hooks that take too long.
                    uiContext?.Post(_ => RequestUnlock(), null);
                }
                if (verdict != KeyVerdict.Allow)
                {
                    return 1;
                }
            }
        }
        return CallNextHookEx(keyboardHook, nCode, wParam, lParam);
    }

    // When the hook swallows a modifier key, Windows' own key state never sees it, so track it here.
    private void TrackModifier(uint vk, bool keyDown)
    {
        if ((Keys)vk is Keys.LControlKey or Keys.RControlKey or Keys.LShiftKey or Keys.RShiftKey or Keys.LMenu or Keys.RMenu)
        {
            if (keyDown)
            {
                heldModifiers.Add(vk);
            }
            else
            {
                heldModifiers.Remove(vk);
            }
        }
    }

    private bool IsHeld(Keys left, Keys right, Keys either) =>
        heldModifiers.Contains((uint)left) || heldModifiers.Contains((uint)right) || GetAsyncKeyState((int)either) < 0;

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (int)wParam is var message
            && (message is WM_LBUTTONDOWN or WM_LBUTTONUP or WM_LBUTTONDBLCLK or WM_RBUTTONDOWN or WM_RBUTTONUP
                    or WM_MBUTTONDOWN or WM_MBUTTONUP or WM_XBUTTONDOWN or WM_XBUTTONUP
                || (options.WatchOnly && message is WM_MOUSEWHEEL or WM_MOUSEHWHEEL)))
        {
            var mouse = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if (ShouldBlockClick(mouse.pt, rightButton: message is WM_RBUTTONDOWN or WM_RBUTTONUP))
            {
                return 1;
            }
        }
        return CallNextHookEx(mouseHook, nCode, wParam, lParam);
    }

    private bool ShouldBlockClick(POINT point, bool rightButton)
    {
        IntPtr window = GetAncestor(WindowFromPoint(point), GA_ROOT);
        if (window == IntPtr.Zero)
        {
            return true;
        }
        uint pid = WindowFinder.GetProcessId(window);
        if (pid == ownPid)
        {
            return false; // the PIN prompt and the blocking screen's unlock button
        }
        if (options.WatchOnly || !IsLockedApp(pid))
        {
            return true; // watch-only mode, or the desktop, taskbar, notifications, other apps
        }

        // Ask the window what is under the cursor. This also finds the Minimize/Close buttons of apps
        // that draw their own title bar, like Zoom, Chrome and Teams.
        IntPtr lParamPoint = (IntPtr)(int)(((uint)point.Y << 16) | ((uint)point.X & 0xFFFF));
        if (SendMessageTimeout(window, WM_NCHITTEST, IntPtr.Zero, lParamPoint, SMTO_ABORTIFHUNG, 100, out IntPtr hit) == IntPtr.Zero)
        {
            return false;
        }
        int area = (int)hit;
        return area is HTCLOSE or HTMINBUTTON or HTSYSMENU || (rightButton && area == HTCAPTION);
    }
}
