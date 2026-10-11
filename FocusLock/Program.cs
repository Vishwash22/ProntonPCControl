using Microsoft.Win32;

namespace FocusLock;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleInstance = new Mutex(true, @"Local\FocusLock.SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("FocusLock is already running.", "FocusLock");
            return;
        }

        // Undo anything a previous run left behind if it crashed or the PC lost power mid-lock.
        SystemRestrictions.Restore();

        // However the app ends, never leave Task Manager disabled or the taskbar hidden.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            SystemRestrictions.Restore();
            MessageBox.Show(e.Exception.ToString(), "FocusLock crashed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.Exit(1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, _) => SystemRestrictions.Restore();
        Application.ApplicationExit += (_, _) => SystemRestrictions.Restore();
        SystemEvents.SessionEnding += (_, _) => SystemRestrictions.Restore();

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
