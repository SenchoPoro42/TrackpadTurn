using TrackpadTurn.Windows.Diagnostics;
using TrackpadTurn.Windows.Input;

namespace TrackpadTurn.Windows.App;

/// <summary>What the app is doing, in words, for the tray tooltip and the settings window.</summary>
internal sealed record AppStatus(string Text, bool IsProblem);

/// <summary>
/// The app: a tray icon, the input thread and the settings window. Closing the window keeps the
/// app running; Quit (tray menu or window) stops it.
/// </summary>
internal sealed class TrayApplication : ApplicationContext
{
    private readonly SettingsStore store = new();
    private readonly AddonInstaller installer = new();
    private readonly InputThread input;
    private readonly NotifyIcon tray;
    private readonly ToolStripMenuItem enabledItem;
    private readonly SynchronizationContext uiContext;
    private readonly RegisteredWaitHandle showRequestWait;
    private SettingsForm? settingsForm;
    private InputStatus inputStatus = new(WowInFront: false, WowNeedsAdministrator: false, TouchpadCount: 0, SwipeCount: 0);
    private bool isShutDown;

    /// <param name="showRequest">Set when TrackpadTurn is started again while it runs: show the window.</param>
    public TrayApplication(EventWaitHandle showRequest)
    {
        uiContext = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(uiContext);

        Log.IsDebugEnabled = store.Settings.DiagnosticLog;
        Log.Info($"TrackpadTurn {AppIcon.Version} started ({Environment.OSVersion}, {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture})");
        if (TouchpadSettings.IgnoresTouchpadWhileTyping() == true)
        {
            Log.Info("Windows touchpad sensitivity is not 'Most sensitive': swipes right after a key press may be ignored");
        }
        StartAtLogin.RefreshPath();

        installer.Refresh(store.Settings.CustomWowFolder);
        var blockedUpdates = installer.UpdateExistingInstalls();

        input = new InputThread(store.Settings.Turn, uiContext, OnInputStatus);
        input.Start();

        enabledItem = new ToolStripMenuItem("Enabled") { CheckOnClick = true, Checked = store.Settings.Turn.IsEnabled };
        enabledItem.CheckedChanged += (_, _) =>
            store.Update(settings => settings with { Turn = settings.Turn with { IsEnabled = enabledItem.Checked } });
        var menu = new ContextMenuStrip();
        menu.Items.Add(enabledItem);
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit TrackpadTurn", null, (_, _) => Quit());

        tray = new NotifyIcon
        {
            Icon = AppIcon.Load(SystemInformation.SmallIconSize),
            ContextMenuStrip = menu,
            Visible = true,
        };
        tray.MouseClick += (_, click) =>
        {
            if (click.Button == MouseButtons.Left)
            {
                ShowSettings();
            }
        };

        store.Changed += OnSettingsChanged;
        Application.ApplicationExit += (_, _) => ShutDown();
        showRequestWait = ThreadPool.RegisterWaitForSingleObject(showRequest,
            (_, _) => uiContext.Post(_ => ShowSettings(), null), null, Timeout.Infinite, executeOnlyOnce: false);
        UpdateStatus();

        // First run: the addon still has to be installed. Blocked updates need a click too.
        if (store.IsFirstRun || blockedUpdates.Count > 0)
        {
            ShowSettings();
        }
    }

    private AppStatus Status
    {
        get
        {
            if (!store.Settings.Turn.IsEnabled)
            {
                return new("Off", IsProblem: false);
            }
            if (inputStatus.TouchpadCount == 0)
            {
                return new("No Precision Touchpad found. TrackpadTurn needs one.", IsProblem: true);
            }
            if (inputStatus.WowNeedsAdministrator)
            {
                return new("WoW runs as administrator, so Windows blocks TrackpadTurn. Run TrackpadTurn as administrator too.", IsProblem: true);
            }
            if (!inputStatus.WowInFront)
            {
                return new("Waiting for World of Warcraft", IsProblem: false);
            }
            return new(inputStatus.SwipeCount == 1 ? "Ready · 1 swipe" : $"Ready · {inputStatus.SwipeCount} swipes", IsProblem: false);
        }
    }

    private void OnInputStatus(InputStatus status)
    {
        inputStatus = status;
        UpdateStatus();
    }

    private void OnSettingsChanged(AppSettings settings)
    {
        input.UpdateSettings(settings.Turn);
        Log.IsDebugEnabled = settings.DiagnosticLog;
        enabledItem.Checked = settings.Turn.IsEnabled;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var status = Status;
        // The tooltip holds at most 127 characters.
        var tooltip = $"TrackpadTurn: {status.Text}";
        tray.Text = tooltip.Length > 127 ? tooltip[..126] + "…" : tooltip;
        settingsForm?.ShowStatus(status);
    }

    private void ShowSettings()
    {
        if (isShutDown)
        {
            return;
        }
        if (settingsForm is null || settingsForm.IsDisposed)
        {
            settingsForm = new SettingsForm(store, installer, Quit);
        }
        settingsForm.ShowStatus(Status);
        settingsForm.Show();
        if (settingsForm.WindowState == FormWindowState.Minimized)
        {
            settingsForm.WindowState = FormWindowState.Normal;
        }
        settingsForm.Activate();
    }

    private void Quit()
    {
        ShutDown();
        ExitThread();
    }

    /// <summary>Releases anything held in WoW, removes the hook and the tray icon.</summary>
    private void ShutDown()
    {
        if (isShutDown)
        {
            return;
        }
        isShutDown = true;
        showRequestWait.Unregister(null);
        input.Dispose();
        tray.Visible = false;
        tray.Dispose();
        settingsForm?.Dispose();
        Log.Info("quit");
        Log.Flush();
    }
}
