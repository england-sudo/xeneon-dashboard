using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace XeneonDash;

public sealed class AppSettings
{
    public int ScreenIndex { get; set; } = -1; // -1 = auto: non-primary screen when one exists
    public bool AlwaysOnTop { get; set; } = true;
    public int RefreshMs { get; set; } = 1000;
    public string ThemeId { get; set; } = "grimoire";
    public int FontScalePct { get; set; } = 100; // 80..130
    public bool StartWithWindows { get; set; } = true;
    public bool CsvLogging { get; set; } = true;
    // Windowed mode: run in a normal resizable window instead of borderless
    // fullscreen. WindowW/H are the start size and the layout-scale surface;
    // WindowX/Y are where the window last sat (-1 = center on first run).
    public bool Windowed { get; set; } = false;
    public int WindowW { get; set; } = 1600;
    public int WindowH { get; set; } = 900;
    public int WindowX { get; set; } = -1;
    public int WindowY { get; set; } = -1;

    public static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XeneonDash", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (s != null) return s;
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

public sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly ThemedCombo _screens = new();
    private readonly ThemedCombo _windowMode = new();
    private readonly ThemedCombo _windowSize = new();
    private readonly List<(int W, int H)> _windowSizeValues = new();
    private readonly ThemedCombo _themes = new();
    private readonly ThemedCombo _refresh = new();
    private readonly ToggleRow _topMost = new() { Text = "Always on top" };
    private readonly ToggleRow _startup = new() { Text = "Start with Windows (as administrator)" };
    private readonly ToggleRow _csv = new() { Text = "Log sensors to CSV" };
    private readonly TrackBar _fontScale = new();
    private readonly Label _fontScaleLabel = new();

    private static readonly int[] RefreshValues = { 250, 500, 1000, 2000, 5000, 10000 };
    private static readonly string[] RefreshLabels =
        { "250 ms", "500 ms", "1 second", "2 seconds", "5 seconds", "10 seconds" };

    /// <summary>True when the user changed theme, text size, or window
    /// mode/size — the app must restart to apply them.</summary>
    public bool NeedsRestart { get; private set; }

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;
        Text = "XeneonDash setup";
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(460, 640);
        Font = Theme.LabelFont;

        Controls.Add(Section("DISPLAY", 12));
        _screens.Location = new Point(24, 32);
        _screens.Width = 412;
        _screens.Items.Add("Auto — use the secondary display when one is connected");
        var screens = Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            var s = screens[i];
            _screens.Items.Add($"Display {i + 1}: {s.Bounds.Width}x{s.Bounds.Height} {(s.Primary ? "(primary)" : "")} — {s.DeviceName}");
        }
        int sel = _settings.ScreenIndex + 1;
        _screens.SelectedIndex = sel >= 0 && sel < _screens.Items.Count ? sel : 0;
        Controls.Add(_screens);

        Controls.Add(Section("WINDOW", 78));
        _windowMode.Location = new Point(24, 98);
        _windowMode.Width = 412;
        _windowMode.Items.Add("Fullscreen — borderless, fills the display");
        _windowMode.Items.Add("Windowed — normal resizable window");
        _windowMode.SelectedIndex = _settings.Windowed ? 1 : 0;
        Controls.Add(_windowMode);

        _windowSize.Location = new Point(24, 134);
        _windowSize.Width = 412;
        int sizeSel = -1;
        foreach (var (w, h) in new[] { (2560, 720), (1920, 1080), (1920, 540), (1600, 900), (1280, 720) })
        {
            _windowSizeValues.Add((w, h));
            _windowSize.Items.Add($"Window size: {w} × {h}");
            if (w == _settings.WindowW && h == _settings.WindowH) sizeSel = _windowSizeValues.Count - 1;
        }
        if (sizeSel < 0)
        {
            // A size remembered from dragging matches no preset; offer it
            // verbatim so a Save can't silently stomp it.
            _windowSizeValues.Insert(0, (_settings.WindowW, _settings.WindowH));
            _windowSize.Items.Insert(0, $"Window size: {_settings.WindowW} × {_settings.WindowH} (last window)");
            sizeSel = 0;
        }
        _windowSize.SelectedIndex = sizeSel;
        Controls.Add(_windowSize);

        Controls.Add(Section("THEME", 180));
        _themes.Location = new Point(24, 200);
        _themes.Width = 412;
        int themeSel = 0;
        for (int i = 0; i < Theme.All.Length; i++)
        {
            _themes.Items.Add(Theme.All[i].Name);
            if (Theme.All[i].Id.Equals(_settings.ThemeId, StringComparison.OrdinalIgnoreCase))
                themeSel = i;
        }
        _themes.SelectedIndex = themeSel;
        Controls.Add(_themes);

        Controls.Add(Section("TEXT SIZE", 246));
        _fontScaleLabel.ForeColor = Theme.Text;
        _fontScaleLabel.Location = new Point(380, 246);
        _fontScaleLabel.AutoSize = true;
        _fontScale.Minimum = 80;
        _fontScale.Maximum = 130;
        _fontScale.TickFrequency = 10;
        _fontScale.SmallChange = 5;
        _fontScale.LargeChange = 10;
        _fontScale.Location = new Point(16, 266);
        _fontScale.Width = 360;
        _fontScale.BackColor = Theme.Bg;
        _fontScale.Value = Math.Clamp(_settings.FontScalePct, 80, 130);
        _fontScale.Scroll += (_, _) => _fontScaleLabel.Text = $"{_fontScale.Value}%";
        _fontScaleLabel.Text = $"{_fontScale.Value}%";
        Controls.Add(_fontScaleLabel);
        Controls.Add(_fontScale);

        Controls.Add(Section("REFRESH RATE", 314));
        _refresh.Location = new Point(24, 334);
        _refresh.Width = 412;
        int refSel = 2;
        for (int i = 0; i < RefreshLabels.Length; i++)
        {
            _refresh.Items.Add(RefreshLabels[i]);
            // Nearest preset (old settings could hold any 250ms step).
            if (Math.Abs(RefreshValues[i] - _settings.RefreshMs) < Math.Abs(RefreshValues[refSel] - _settings.RefreshMs))
                refSel = i;
        }
        _refresh.SelectedIndex = refSel;
        Controls.Add(_refresh);

        Controls.Add(Section("OPTIONS", 380));
        _topMost.Location = new Point(24, 400);
        _topMost.Width = 412;
        _topMost.Checked = _settings.AlwaysOnTop;
        _startup.Location = new Point(24, 434);
        _startup.Width = 412;
        _startup.Checked = StartupManager.IsEnabled(); // reflect the actual task, not just the saved wish
        _csv.Location = new Point(24, 468);
        _csv.Width = 412;
        _csv.Checked = _settings.CsvLogging;
        Controls.Add(_topMost);
        Controls.Add(_startup);
        Controls.Add(_csv);

        var lblNote = new Label
        {
            Text = "Theme, text size, and window mode/size apply after the app restarts.\r\nWindowed mode starts at the picked size, then drag to resize; position is remembered.\r\nCSV logs one row every 5 seconds to Documents\\XeneonDash\\logs."
                   + (StartupManager.IsElevated
                       ? ""
                       : "\r\nNot running as administrator — some sensors (GPU power, fans, drive health) may be unavailable."),
            ForeColor = Theme.Dim,
            Location = new Point(24, 508),
            Size = new Size(412, 60),
        };
        Controls.Add(lblNote);

        var btnSave = new Button { Text = "Save", DialogResult = DialogResult.OK, Location = new Point(24, 574), Size = new Size(130, 44) };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(166, 574), Size = new Size(130, 44) };
        var btnExit = new Button { Text = "Exit app", Location = new Point(308, 574), Size = new Size(128, 44) };
        btnExit.Click += (_, _) => Application.Exit();
        foreach (var b in new[] { btnSave, btnCancel, btnExit })
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Theme.Border;
            b.BackColor = Theme.Card;
            b.ForeColor = Theme.Text;
        }

        AcceptButton = btnSave;
        CancelButton = btnCancel;
        Controls.Add(btnSave);
        Controls.Add(btnCancel);
        Controls.Add(btnExit);
    }

    private static Label Section(string text, int y) =>
        new()
        {
            Text = text,
            ForeColor = Theme.Gold,
            Font = Theme.LabelFont,
            Location = new Point(24, y),
            AutoSize = true,
        };

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK)
        {
            _settings.ScreenIndex = _screens.SelectedIndex - 1;
            _settings.AlwaysOnTop = _topMost.Checked;
            _settings.RefreshMs = RefreshValues[Math.Clamp(_refresh.SelectedIndex, 0, RefreshValues.Length - 1)];

            bool newStartup = _startup.Checked;
            if (newStartup != StartupManager.IsEnabled())
            {
                // The logon task runs elevated, so creating/deleting it needs
                // admin: apply directly when elevated, else via the one-time
                // UAC helper. Afterwards save what actually took effect, never
                // what the checkbox merely claims.
                if (StartupManager.IsElevated) StartupManager.SetEnabled(newStartup);
                else StartupManager.ApplyWithElevation(newStartup);
            }
            _settings.StartWithWindows = StartupManager.IsEnabled();
            _settings.CsvLogging = _csv.Checked;

            string newThemeId = Theme.All[_themes.SelectedIndex].Id;
            int newScale = _fontScale.Value;
            bool newWindowed = _windowMode.SelectedIndex == 1;
            var (newWinW, newWinH) = _windowSizeValues[Math.Clamp(_windowSize.SelectedIndex, 0, _windowSizeValues.Count - 1)];
            NeedsRestart = !newThemeId.Equals(_settings.ThemeId, StringComparison.OrdinalIgnoreCase)
                           || newScale != _settings.FontScalePct
                           || newWindowed != _settings.Windowed
                           || newWinW != _settings.WindowW
                           || newWinH != _settings.WindowH;
            _settings.ThemeId = newThemeId;
            _settings.FontScalePct = newScale;
            _settings.Windowed = newWindowed;
            _settings.WindowW = newWinW;
            _settings.WindowH = newWinH;
        }
        base.OnFormClosing(e);
    }
}
