using System.Diagnostics;
using System.Globalization;
using TrackpadTurn.Core;
using TrackpadTurn.Windows.Diagnostics;
using TrackpadTurn.Windows.Input;

namespace TrackpadTurn.Windows.App;

/// <summary>
/// The settings window: the same controls as the Mac app's menu bar panel, plus the addon
/// installer and the diagnostic log.
/// </summary>
internal sealed class SettingsForm : Form
{
    private const int ContentWidth = 380;

    private readonly SettingsStore store;
    private readonly AddonInstaller installer;
    private readonly Label statusLabel;
    private readonly CheckBox enabledBox;
    private readonly SpeedSlider turnSpeed;
    private readonly SpeedSlider zoomSpeed;
    private readonly CheckBox tiltBox;
    private readonly CheckBox pinchBox;
    private readonly CheckBox holdToRunBox;
    private readonly CheckBox reverseHorizontalBox;
    private readonly CheckBox reverseVerticalBox;
    private readonly TableLayoutPanel addonRows;
    private readonly Label addonMessage;
    private readonly CheckBox startAtLoginBox;
    private readonly CheckBox diagnosticLogBox;
    private bool isApplyingSettings;

    public SettingsForm(SettingsStore store, AddonInstaller installer, Action quit)
    {
        this.store = store;
        this.installer = installer;

        Text = "TrackpadTurn";
        Icon = AppIcon.Load(new Size(32, 32));
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };
        Controls.Add(layout);
        var boldFont = new Font(Font, FontStyle.Bold);

        // Header and status
        var title = new Label { Text = "TrackpadTurn", Font = new Font(Font.FontFamily, Font.Size * 1.5f, FontStyle.Bold), AutoSize = true };
        layout.Controls.Add(Row(title, new Label { Text = $"v{AppIcon.Version}", ForeColor = SystemColors.GrayText, AutoSize = true, Anchor = AnchorStyles.Right | AnchorStyles.Bottom }));
        layout.Controls.Add(new Label { Text = "Two-finger touchpad turning for World of Warcraft", ForeColor = SystemColors.GrayText, AutoSize = true });
        statusLabel = new Label { AutoSize = true, MaximumSize = new Size(ContentWidth, 0), Margin = new Padding(3, 10, 3, 3) };
        layout.Controls.Add(statusLabel);
        if (TouchpadSettings.IgnoresTouchpadWhileTyping() == true)
        {
            layout.Controls.Add(Note("Tip: to swipe while you hold a movement key, set Windows Settings → Bluetooth & devices → "
                + "Touchpad → Taps → Touchpad sensitivity to \"Most sensitive\"."));
        }

        enabledBox = Check("Enabled", (settings, on) => settings with { IsEnabled = on });
        enabledBox.Margin = new Padding(3, 10, 3, 3);
        layout.Controls.Add(enabledBox);

        // Speeds
        turnSpeed = new SpeedSlider("Turn speed", TurnSettings.TurnSpeedRange, value => UpdateTurn(settings => settings with { TurnSpeed = value }));
        zoomSpeed = new SpeedSlider("Zoom speed", TurnSettings.ZoomSpeedRange, value => UpdateTurn(settings => settings with { ZoomSpeed = value }));
        layout.Controls.Add(Section("Speed", boldFont));
        layout.Controls.Add(turnSpeed);
        layout.Controls.Add(zoomSpeed);

        // Gestures
        layout.Controls.Add(Section("Gestures", boldFont));
        tiltBox = Check("Tilt camera with up/down swipes", (settings, on) => settings with { TiltEnabled = on });
        pinchBox = Check("Pinch to zoom", (settings, on) => settings with { PinchZoomEnabled = on });
        holdToRunBox = Check("Hold a two-finger click to run", (settings, on) => settings with { PressToRunEnabled = on });
        reverseHorizontalBox = Check("Reverse left/right", (settings, on) => settings with { ReverseHorizontal = on });
        reverseVerticalBox = Check("Reverse up/down", (settings, on) => settings with { ReverseVertical = on });
        foreach (var box in new[] { tiltBox, pinchBox, holdToRunBox, reverseHorizontalBox, reverseVerticalBox })
        {
            layout.Controls.Add(box);
        }

        // WoW addon
        layout.Controls.Add(Row(Section("WoW addon", boldFont), new Label
        {
            Text = installer.BundledVersion is { } version ? $"v{version}" : "",
            ForeColor = SystemColors.GrayText,
            AutoSize = true,
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
        }));
        addonRows = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Fill };
        addonRows.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        addonRows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        addonRows.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.Controls.Add(addonRows);
        var chooseFolder = new Button { Text = "Choose WoW folder…", AutoSize = true };
        chooseFolder.Click += (_, _) => ChooseWowFolder();
        layout.Controls.Add(chooseFolder);
        addonMessage = Note("");
        layout.Controls.Add(addonMessage);

        // App
        layout.Controls.Add(Section("App", boldFont));
        startAtLoginBox = new CheckBox { Text = "Start at login", AutoSize = true, Checked = StartAtLogin.IsEnabled };
        startAtLoginBox.CheckedChanged += (_, _) => StartAtLogin.Set(startAtLoginBox.Checked);
        layout.Controls.Add(startAtLoginBox);
        diagnosticLogBox = new CheckBox { Text = "Write a diagnostic log", AutoSize = true };
        diagnosticLogBox.CheckedChanged += (_, _) =>
        {
            if (!isApplyingSettings)
            {
                store.Update(settings => settings with { DiagnosticLog = diagnosticLogBox.Checked });
            }
        };
        var openLog = new LinkLabel { Text = "Open log folder", AutoSize = true, Anchor = AnchorStyles.Left };
        openLog.LinkClicked += (_, _) => OpenLogFolder();
        layout.Controls.Add(Row(diagnosticLogBox, openLog));

        // Buttons
        var quitButton = new Button { Text = "Quit TrackpadTurn", AutoSize = true };
        quitButton.Click += (_, _) => quit();
        var closeButton = new Button { Text = "Close", AutoSize = true };
        closeButton.Click += (_, _) => Hide();
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 14, 0, 0) };
        buttons.Controls.Add(closeButton);
        buttons.Controls.Add(quitButton);
        layout.Controls.Add(buttons);
        CancelButton = closeButton;

        ApplySettings(store.Settings);
        RefreshAddons();
        store.Changed += ApplySettings;
    }

    public void ShowStatus(AppStatus status)
    {
        statusLabel.Text = $"● {status.Text}";
        statusLabel.ForeColor = status.IsProblem ? ProblemColor : SystemColors.ControlText;
    }

    private static Color ProblemColor =>
#pragma warning disable WFO5001 // Dark mode is still marked experimental in WinForms.
        Application.IsDarkModeEnabled ? Color.FromArgb(255, 128, 128) : Color.Firebrick;
#pragma warning restore WFO5001

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // The window only hides; the app keeps running in the tray.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            store.Changed -= ApplySettings;
        }
        base.Dispose(disposing);
    }

    // Settings

    private void ApplySettings(AppSettings settings)
    {
        isApplyingSettings = true;
        var turn = settings.Turn;
        enabledBox.Checked = turn.IsEnabled;
        turnSpeed.Value = turn.TurnSpeed;
        zoomSpeed.Value = turn.ZoomSpeed;
        tiltBox.Checked = turn.TiltEnabled;
        pinchBox.Checked = turn.PinchZoomEnabled;
        holdToRunBox.Checked = turn.PressToRunEnabled;
        reverseHorizontalBox.Checked = turn.ReverseHorizontal;
        reverseVerticalBox.Checked = turn.ReverseVertical;
        diagnosticLogBox.Checked = settings.DiagnosticLog;
        isApplyingSettings = false;
    }

    private void UpdateTurn(Func<TurnSettings, TurnSettings> change)
    {
        if (!isApplyingSettings)
        {
            store.Update(settings => settings with { Turn = change(settings.Turn) });
        }
    }

    private CheckBox Check(string text, Func<TurnSettings, bool, TurnSettings> change)
    {
        var box = new CheckBox { Text = text, AutoSize = true };
        box.CheckedChanged += (_, _) => UpdateTurn(settings => change(settings, box.Checked));
        return box;
    }

    // Addon

    private void RefreshAddons()
    {
        addonRows.SuspendLayout();
        addonRows.Controls.Clear();
        addonRows.RowCount = 0;
        if (installer.Flavors.Count == 0)
        {
            var none = Note("No World of Warcraft install found in Program Files. Choose its folder below.");
            addonRows.Controls.Add(none);
            addonRows.SetColumnSpan(none, 3);
        }
        foreach (var flavor in installer.Flavors)
        {
            var state = flavor.InstalledVersion switch
            {
                null => "Not installed",
                var version when version == installer.BundledVersion => "Installed",
                var version => $"v{version} installed",
            };
            var action = flavor.InstalledVersion is null ? "Install" : installer.NeedsInstall(flavor) ? "Update" : "Reinstall";
            var button = new Button { Text = action, AutoSize = true };
            button.Click += (_, _) => Install(flavor);
            addonRows.Controls.Add(new Label { Text = flavor.DisplayName, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 12, 3) });
            addonRows.Controls.Add(new Label { Text = state, AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = SystemColors.GrayText });
            addonRows.Controls.Add(button);
        }
        addonRows.ResumeLayout();
        if (installer.LastError is { } error)
        {
            addonMessage.Text = error;
            addonMessage.ForeColor = ProblemColor;
        }
    }

    private void Install(GameFlavor flavor)
    {
        var outcome = installer.Install(flavor);
        if (outcome == InstallOutcome.NeedsAdministrator)
        {
            var answer = MessageBox.Show(this,
                $"Windows needs administrator approval to write to:\n{flavor.AddOnsFolder}\n\nInstall the addon as administrator?",
                "TrackpadTurn", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer == DialogResult.Yes)
            {
                outcome = installer.InstallAsAdministrator(flavor);
            }
        }
        if (outcome == InstallOutcome.Installed)
        {
            addonMessage.Text = $"Installed in {flavor.DisplayName}. Restart WoW once so it finds the addon.";
            addonMessage.ForeColor = SystemColors.GrayText;
        }
        RefreshAddons();
    }

    private void ChooseWowFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose your World of Warcraft folder",
            UseDescriptionForTitle = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        var root = AddonInstaller.RootOf(dialog.SelectedPath);
        store.Update(settings => settings with { CustomWowFolder = root });
        installer.Refresh(root);
        addonMessage.Text = installer.Flavors.Count == 0 ? $"No game flavor folders (like _retail_) in {root}." : "";
        addonMessage.ForeColor = SystemColors.GrayText;
        RefreshAddons();
    }

    private static void OpenLogFolder()
    {
        Directory.CreateDirectory(Log.Folder);
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { Log.Folder }, UseShellExecute = false });
    }

    // Layout helpers

    private static Label Section(string text, Font font) =>
        new() { Text = text, Font = font, AutoSize = true, Margin = new Padding(3, 14, 3, 3) };

    private static Label Note(string text) =>
        new() { Text = text, ForeColor = SystemColors.GrayText, AutoSize = true, MaximumSize = new Size(ContentWidth, 0) };

    /// <summary>Two controls on one line: the first on the left, the second pushed right.</summary>
    private static TableLayoutPanel Row(Control left, Control right)
    {
        var row = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.Controls.Add(left);
        row.Controls.Add(right);
        return row;
    }

    /// <summary>A labelled slider over a speed range, in steps of 0.05×.</summary>
    private sealed class SpeedSlider : TableLayoutPanel
    {
        private const double Step = 0.05;
        private readonly TrackBar trackBar;
        private readonly Label valueLabel;
        private readonly Action<double> changed;
        private bool isSetting;

        public SpeedSlider(string title, SpeedRange range, Action<double> changed)
        {
            this.changed = changed;
            ColumnCount = 2;
            AutoSize = true;
            Dock = DockStyle.Fill;
            Margin = Padding.Empty;
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            valueLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Right };
            trackBar = new TrackBar
            {
                Minimum = (int)Math.Round(range.Min / Step),
                Maximum = (int)Math.Round(range.Max / Step),
                TickFrequency = 5,
                SmallChange = 1,
                LargeChange = 5,
                Width = ContentWidth,
                Dock = DockStyle.Fill,
            };
            trackBar.ValueChanged += (_, _) =>
            {
                ShowValue();
                if (!isSetting)
                {
                    this.changed(trackBar.Value * Step);
                }
            };
            Controls.Add(new Label { Text = title, AutoSize = true, Anchor = AnchorStyles.Left });
            Controls.Add(valueLabel);
            Controls.Add(trackBar);
            SetColumnSpan(trackBar, 2);
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public double Value
        {
            get => trackBar.Value * Step;
            set
            {
                isSetting = true;
                trackBar.Value = Math.Clamp((int)Math.Round(value / Step), trackBar.Minimum, trackBar.Maximum);
                ShowValue();
                isSetting = false;
            }
        }

        private void ShowValue() => valueLabel.Text = (trackBar.Value * Step).ToString("0.00", CultureInfo.InvariantCulture) + "×";
    }
}
