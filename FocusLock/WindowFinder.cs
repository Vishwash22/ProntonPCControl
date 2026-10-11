using System.Diagnostics;
using System.Text;
using static FocusLock.NativeMethods;

namespace FocusLock;

internal sealed record AppWindow(IntPtr Handle, string Title, string ProcessName);

/// <summary>Caches process names by process id; looking them up on every click or tick is too slow.</summary>
internal sealed class ProcessNames
{
    private readonly Dictionary<uint, (string Name, long CheckedAt)> cache = new();

    public string Get(uint pid)
    {
        long now = Environment.TickCount64;
        if (cache.TryGetValue(pid, out var entry) && now - entry.CheckedAt < 5000)
        {
            return entry.Name;
        }

        string name;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            name = process.ProcessName;
        }
        catch (Exception)
        {
            name = "";
        }

        cache[pid] = (name, now);
        return name;
    }
}

internal static class WindowFinder
{
    // Shell processes own the taskbar, desktop and Start menu, so locking to them would unlock everything.
    private static readonly HashSet<string> ShellProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp",
        "TextInputHost", "LockApp",
    };

    public static uint GetProcessId(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid;
    }

    /// <summary>Visible, titled top-level windows of other applications that a parent can lock to.</summary>
    public static List<AppWindow> ListAppWindows(ProcessNames names)
    {
        uint ownPid = (uint)Environment.ProcessId;
        var windows = new List<AppWindow>();
        EnumWindows((hwnd, _) =>
        {
            if (IsAppWindow(hwnd))
            {
                uint pid = GetProcessId(hwnd);
                string name = names.Get(pid);
                if (pid != ownPid && name != "" && !ShellProcesses.Contains(name))
                {
                    windows.Add(new AppWindow(hwnd, GetTitle(hwnd), name));
                }
            }
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    /// <summary>The largest app window of the named process, or zero when it has none (e.g. it was closed).</summary>
    public static IntPtr FindMainWindow(string processName, ProcessNames names)
    {
        IntPtr best = IntPtr.Zero;
        long bestArea = -1;
        EnumWindows((hwnd, _) =>
        {
            if (IsAppWindow(hwnd)
                && string.Equals(names.Get(GetProcessId(hwnd)), processName, StringComparison.OrdinalIgnoreCase)
                && GetWindowRect(hwnd, out RECT r))
            {
                long area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
                if (area > bestArea)
                {
                    best = hwnd;
                    bestArea = area;
                }
            }
            return true;
        }, IntPtr.Zero);
        return best;
    }

    /// <summary>
    /// Brings a window to the front. Windows normally refuses to let a background process do this,
    /// so we temporarily share input state with the current foreground thread, and as a last resort
    /// tap Alt, which lifts the foreground lock.
    /// </summary>
    public static void ForceForeground(IntPtr hwnd)
    {
        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, SW_RESTORE);
        }

        IntPtr foreground = GetForegroundWindow();
        if (foreground == hwnd)
        {
            return;
        }

        uint ownThread = GetCurrentThreadId();
        uint foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);
        bool attached = foregroundThread != 0 && foregroundThread != ownThread
            && AttachThreadInput(ownThread, foregroundThread, true);
        try
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(ownThread, foregroundThread, false);
            }
        }

        if (GetForegroundWindow() != hwnd)
        {
            keybd_event(VK_MENU, 0, 0, InjectedByFocusLock);
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, InjectedByFocusLock);
            SetForegroundWindow(hwnd);
        }
    }

    public static void KeepTopMost(IntPtr hwnd) =>
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);

    private static bool IsAppWindow(IntPtr hwnd) =>
        IsWindowVisible(hwnd)
        && GetWindow(hwnd, GW_OWNER) == IntPtr.Zero
        && (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) == 0
        && !IsCloaked(hwnd)
        && GetWindowTextLength(hwnd) > 0;

    // Suspended Store apps and windows on other virtual desktops are "cloaked": visible to the API but not on screen.
    private static bool IsCloaked(IntPtr hwnd) =>
        DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    private static string GetTitle(IntPtr hwnd)
    {
        var text = new StringBuilder(GetWindowTextLength(hwnd) + 1);
        GetWindowText(hwnd, text, text.Capacity);
        return text.ToString();
    }
}
