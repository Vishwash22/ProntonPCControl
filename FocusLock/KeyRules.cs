namespace FocusLock;

internal enum KeyVerdict
{
    Allow,
    Block,
    ParentUnlock,
}

/// <summary>Decides which keyboard shortcuts a locked child may use.</summary>
internal static class KeyRules
{
    private const uint Tab = 0x09, Escape = 0x1B, Space = 0x20, F4 = 0x73, Q = 0x51, U = 0x55, W = 0x57;
    private const uint LeftWindows = 0x5B, RightWindows = 0x5C;

    /// <param name="blockAll">Watch-only mode: every key is swallowed except the parent unlock shortcut.</param>
    public static KeyVerdict Evaluate(uint vk, bool keyDown, bool alt, bool ctrl, bool shift, bool blockAll)
    {
        if (keyDown && vk == U && ctrl && alt && shift)
        {
            return KeyVerdict.ParentUnlock;
        }

        // Swallow both press and release so the Start menu never opens and no Win+ shortcut works
        // (Win+D, Win+M, Win+Tab, Win+R, Win+Down, ...). Win+L is handled by a policy instead.
        if (blockAll || vk is LeftWindows or RightWindows)
        {
            return KeyVerdict.Block;
        }

        if (!keyDown)
        {
            return KeyVerdict.Allow;
        }

        bool block = vk switch
        {
            Tab => alt,             // Alt+Tab switches apps
            Escape => alt || ctrl,  // Alt+Esc, Ctrl+Esc (Start), Ctrl+Shift+Esc (Task Manager)
            F4 => alt || ctrl,      // Alt+F4 closes the window, Ctrl+F4 closes a tab
            Space => alt,           // Alt+Space opens the window menu (Minimize/Close)
            Q => alt || ctrl,       // Alt+Q leaves a Zoom meeting, Ctrl+Q quits many apps
            W => ctrl,              // Ctrl+W / Ctrl+Shift+W close tabs and windows
            _ => false,
        };
        return block ? KeyVerdict.Block : KeyVerdict.Allow;
    }
}
