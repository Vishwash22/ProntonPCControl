using System.Text.Json;
using Microsoft.Win32;
using static FocusLock.NativeMethods;

namespace FocusLock;

/// <summary>
/// Turns off the escape routes a keyboard hook cannot block: the Ctrl+Alt+Del screen's Task Manager,
/// Lock, Sign out and Change password options, plus the taskbar. The previous values are saved to a
/// file first, so they are restored on unlock, or on the next start if FocusLock crashed.
/// </summary>
internal static class SystemRestrictions
{
    private const string SystemPolicies = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string ExplorerPolicies = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";

    private static readonly (string Key, string Name)[] Policies =
    {
        (SystemPolicies, "DisableTaskMgr"),
        (SystemPolicies, "DisableLockWorkstation"),
        (SystemPolicies, "DisableChangePassword"),
        (ExplorerPolicies, "NoLogoff"),
    };

    private static readonly string[] TaskbarClasses = { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

    private static string RestoreFile => Path.Combine(Settings.Folder, "restore.json");

    /// <summary>Applies the restrictions and returns a warning for each one that could not be applied.</summary>
    public static List<string> Apply()
    {
        Restore();

        var previous = new Dictionary<string, int?>();
        foreach (var (key, name) in Policies)
        {
            using var subKey = Registry.CurrentUser.OpenSubKey(key);
            previous[key + "|" + name] = subKey?.GetValue(name) as int?;
        }
        Directory.CreateDirectory(Settings.Folder);
        File.WriteAllText(RestoreFile, JsonSerializer.Serialize(previous));

        var warnings = new List<string>();
        foreach (var (key, name) in Policies)
        {
            try
            {
                using var subKey = Registry.CurrentUser.CreateSubKey(key);
                subKey.SetValue(name, 1, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                warnings.Add($"Could not set {name}: {ex.Message}");
            }
        }

        HideTaskbars();
        return warnings;
    }

    /// <summary>Puts back everything <see cref="Apply"/> changed. Does nothing when nothing is applied.</summary>
    public static void Restore()
    {
        if (!File.Exists(RestoreFile))
        {
            return;
        }

        SetTaskbarsVisible(true);
        try
        {
            var previous = JsonSerializer.Deserialize<Dictionary<string, int?>>(File.ReadAllText(RestoreFile)) ?? new();
            foreach (var (id, value) in previous)
            {
                string[] parts = id.Split('|');
                using var subKey = Registry.CurrentUser.CreateSubKey(parts[0]);
                if (value is int v)
                {
                    subKey.SetValue(parts[1], v, RegistryValueKind.DWord);
                }
                else
                {
                    subKey.DeleteValue(parts[1], throwOnMissingValue: false);
                }
            }
            File.Delete(RestoreFile);
        }
        catch (Exception)
        {
            // Keep the file so the next start tries again.
        }
    }

    /// <summary>Called on every tick because Explorer re-creates the taskbar when it restarts.</summary>
    public static void HideTaskbars() => SetTaskbarsVisible(false);

    private static void SetTaskbarsVisible(bool visible)
    {
        foreach (string taskbarClass in TaskbarClasses)
        {
            IntPtr hwnd = IntPtr.Zero;
            while ((hwnd = FindWindowEx(IntPtr.Zero, hwnd, taskbarClass, null)) != IntPtr.Zero)
            {
                if (IsWindowVisible(hwnd) != visible)
                {
                    ShowWindow(hwnd, visible ? SW_SHOW : SW_HIDE);
                }
            }
        }
    }
}
