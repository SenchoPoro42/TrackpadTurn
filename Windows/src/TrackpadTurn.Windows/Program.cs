using TrackpadTurn.Windows.App;
using TrackpadTurn.Windows.Diagnostics;

namespace TrackpadTurn.Windows;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Helper modes: an elevated addon install, and the CI check of the Win32 plumbing.
        if (args is [AddonInstaller.InstallArgument, var flavorFolder])
        {
            return AddonInstaller.InstallFromCommandLine(flavorFolder);
        }
        if (args is [SelfTest.Argument, var resultFile])
        {
            return SelfTest.Run(resultFile);
        }

        // One instance per user session. Starting it again opens the running one's window.
        Mutex instance;
        EventWaitHandle showRequest;
        bool isFirstInstance;
        try
        {
            instance = new Mutex(initiallyOwned: true, @"Local\TrackpadTurn.Running", out isFirstInstance);
            showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\TrackpadTurn.ShowSettings");
        }
        catch (UnauthorizedAccessException)
        {
            // A copy running as administrator owns them, and Windows keeps a normal process out.
            MessageBox.Show("TrackpadTurn is already running as administrator. Use its tray icon.", "TrackpadTurn",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        using var ownedInstance = instance;
        using var ownedShowRequest = showRequest;
        if (!isFirstInstance)
        {
            showRequest.Set();
            return 0;
        }

        Log.Start();
        Application.ThreadException += (_, error) => Log.Error("unhandled UI exception", error.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, error) =>
        {
            Log.Error("unhandled exception", error.ExceptionObject as Exception);
            Log.Flush();
        };

        ApplicationConfiguration.Initialize();
#pragma warning disable WFO5001 // Dark mode is still marked experimental in WinForms.
        Application.SetColorMode(SystemColorMode.System);
#pragma warning restore WFO5001
        Application.Run(new TrayApplication(showRequest));
        return 0;
    }
}
