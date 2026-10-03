using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace XeneonDash;

public sealed class MainForm : Form
{
    private readonly StatsProvider _provider;
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly AppSettings _settings;
    private readonly Panel _content = new();
    private readonly HeaderPanel _top = new();
    private readonly HistoryData _history = new();
    private readonly SessionStats _session = new();
    private readonly CsvLogger _csv = new();
    private string _clockText = "";
    private string _dateText = "";
    private int _pageIndex;
    private readonly bool _layoutDump;
    private readonly List<NavButton> _nav = new();
    private readonly UserControl[] _pages;

    public MainForm(AppSettings settings, bool demo, bool layoutDump = false)
    {
        _layoutDump = layoutDump;
        _settings = settings;
        // Global layout scale BEFORE any control exists: the UI is composed
        // for a 2560x720 surface; scale the whole composition (smaller of
        // the two ratios) so it fits any surface. Windowed mode scales to
        // the saved window size instead of the target display.
        {
            float surfaceW, surfaceH;
            if (settings.Windowed)
            {
                surfaceW = Math.Max(settings.WindowW, 640);
                surfaceH = Math.Max(settings.WindowH, 360);
            }
            else
            {
                var t = TargetScreen(settings);
                surfaceW = t.Bounds.Width;
                surfaceH = t.Bounds.Height;
            }
            Theme.LayoutScale = Math.Clamp(
                Math.Min(surfaceW / 2560f, surfaceH / 720f), 0.1f, 4f);
        }
        _provider = new StatsProvider(demo);
        _pages = new UserControl[] { new OverviewPage(), new CpuPage(), new GpuPage(), new SystemPage(), new HistoryPage() };
        if (_pages[4] is HistoryPage historyPage)
            historyPage.ResetRequested += () => { _session.Reset(); _history.Clear(); };
        StatRow.Session = _session;
        _csv.Enabled = _settings.CsvLogging;

        // Belt-and-braces icon: the exe embeds icon.ico via /win32icon, but
        // Windows aggressively caches file icons, so also set the window icon
        // from the shipped file at runtime. Covers taskbar + Alt-Tab either way.
        try
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "icon.ico");
            if (File.Exists(iconPath)) Icon = new Icon(iconPath);
        }
        catch { }

        // Start-with-Windows: the setting is the source of truth. Always clear
        // the old registry entry (it launched unelevated — no GPU sensors), and
        // when running elevated keep the logon task pointed at this exe path
        // (heals after the exe moves). When not elevated we can't touch the
        // task; SETUP offers the one-time UAC apply instead of prompting here.
        try
        {
            StartupManager.RemoveLegacyRunEntry();
            if (StartupManager.IsElevated)
            {
                if (_settings.StartWithWindows) StartupManager.SetEnabled(true);
                else if (StartupManager.IsEnabled()) StartupManager.SetEnabled(false);
            }
        }
        catch { }

        FormBorderStyle = settings.Windowed ? FormBorderStyle.Sizable : FormBorderStyle.None;
        if (settings.Windowed) MinimumSize = new Size(800, 450);
        BackColor = Theme.Bg;
        StartPosition = FormStartPosition.Manual;
        Text = demo ? "XeneonDash (demo)" : "XeneonDash";
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        BuildChrome();
        ApplySettings();
        // Bounds were set in the constructor, before this window landed on
        // its target display. With per-monitor DPI, arriving on a screen
        // with different scaling can leave the window resized past the
        // screen edges (nav/buttons clipped). Re-pin to the exact screen
        // bounds once the DPI context has settled, and again if the
        // window ever moves to another display.
        Shown += (_, _) =>
        {
            ApplySettings();
            if (_layoutDump) WriteLayoutDump();
        };
        DpiChanged += (_, _) =>
        {
            ApplySettings();
            // Children were autoscaled to the new DPI; cached design
            // heights no longer describe them.
            void ResetCards(Control parent)
            {
                foreach (Control c in parent.Controls)
                {
                    if (c is Card card) card.ResetDesignCache();
                    ResetCards(c);
                }
            }
            ResetCards(this);
        };

        foreach (var p in _pages)
        {
            p.Dock = DockStyle.Fill;
            p.Visible = false;
            _content.Controls.Add(p);
        }
        ShowPage(0);

        // Full-bleed themed backgrounds behind the cards: hook plain
        // container panels (never the custom-painted controls).
        HookBackgrounds(_content);

        _timer.Interval = Math.Clamp(settings.RefreshMs, 250, 10000);
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();
    }

    /// <summary>Top strip: fully paint-drawn title/machine left, clock/date
    /// right-aligned against the right edge. No Label controls, so text
    /// measuring quirks can't push anything off-screen.</summary>
    private sealed class HeaderPanel : Panel
    {
        public HeaderPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
    }

    private void BuildChrome()
    {
        var top = _top;
        top.Height = Theme.Scale(92);
        top.Dock = DockStyle.Top;
        top.BackColor = Theme.Bg;
        top.Paint += (_, e) => PaintHeader(e.Graphics, top);
        top.Controls.Add(new Divider { Dock = DockStyle.Bottom, FillColor = Theme.Bg });

        Panel? navPanel = null;
        if (Theme.Current.NavLocation == NavPlace.InHeader)
        {
            // Tabs live inside the header strip; click them there.
            top.Cursor = Cursors.Hand;
            top.MouseClick += (_, e) => HeaderTabClick(e.Location);
        }
        else
        {
            navPanel = new Panel { Height = Theme.Scale(96), Dock = DockStyle.Bottom, BackColor = Theme.Bg };
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Theme.Bg,
                Padding = new Padding(Theme.Scale(24), Theme.Scale(8), Theme.Scale(24), Theme.Scale(8)),
                WrapContents = false,
            };
            string[] names = { "OVERVIEW", "CPU", "GPU", "SYSTEM", "HISTORY" };
            for (int i = 0; i < names.Length; i++)
            {
                var b = new NavButton(names[i]) { Width = Theme.Scale(230), Height = Theme.Scale(80), Margin = new Padding(Theme.Scale(8), 0, Theme.Scale(8), 0) };
                int idx = i;
                b.Click += (_, _) => ShowPage(idx);
                _nav.Add(b);
                flow.Controls.Add(b);
            }
            var setup = new NavButton("SETUP") { Width = Theme.Scale(170), Height = Theme.Scale(80), Margin = new Padding(Theme.Scale(8), 0, Theme.Scale(8), 0) };
            setup.Click += (_, _) => OpenSettings();
            flow.Controls.Add(setup);
            navPanel.Controls.Add(flow);
            navPanel.Controls.Add(new Divider { Dock = DockStyle.Top, FillColor = Theme.Bg });
        }

        _content.Dock = DockStyle.Fill;
        _content.BackColor = Theme.Bg;
        _content.Padding = new Padding(Theme.Scale(16), Theme.Scale(4), Theme.Scale(16), Theme.Scale(8));

        // Docking resolves from the last-added control backwards, so the
        // Fill region must be added FIRST: header and nav claim their edges
        // and the content fills only what remains. Adding Fill after the
        // nav let the content run behind the bottom panel selector and
        // hide the bottom rows of every card.
        Controls.Add(_content);
        Controls.Add(top);
        if (navPanel != null) Controls.Add(navPanel);
    }

    private void HookBackgrounds(Control parent)
    {
        foreach (Control c in parent.Controls)
        {
            var t = c.GetType();
            if (t == typeof(Panel) || t == typeof(TableLayoutPanel) || c is UserControl)
                c.Paint += (_, e) => Theme.PaintBackground(e.Graphics, c.ClientRectangle, FormRelative(c));
            HookBackgrounds(c);
        }
    }

    private Point FormRelative(Control c)
    {
        int x = 0, y = 0;
        for (var p = c; p != null && p != this; p = p.Parent) { x += p.Left; y += p.Top; }
        return new Point(x, y);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        Theme.PaintBackground(e.Graphics, ClientRectangle, Point.Empty);
    }

    private void HeaderTabClick(Point pt)
    {
        if (Theme.Current.NavLocation != NavPlace.InHeader) return;
        var rects = Theme.LayoutHeaderTabs(new Rectangle(0, 0, _top.Width, _top.Height), 6);
        for (int i = 0; i < rects.Length; i++)
        {
            if (!rects[i].Contains(pt)) continue;
            if (i < 5) ShowPage(i); else OpenSettings();
            break;
        }
    }

    private const TextFormatFlags NoPrefix = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    private void PaintHeader(Graphics g, Panel top)
    {
        Theme.PaintHeader(g, new HeaderInfo
        {
            Bounds = new Rectangle(0, 0, top.Width, top.Height),
            Title = "XENEON DASH",
            Machine = Environment.MachineName.ToUpperInvariant(),
            Clock = _clockText,
            Date = _dateText,
            Tabs = new[] { "OVERVIEW", "CPU", "GPU", "SYSTEM", "HISTORY", "SETUP" },
            ActiveTab = _pageIndex,
        });
    }

    private static Screen TargetScreen(AppSettings settings)
    {
        var screens = Screen.AllScreens;
        if (settings.ScreenIndex >= 0 && settings.ScreenIndex < screens.Length)
            return screens[settings.ScreenIndex];
        return screens.FirstOrDefault(s => !s.Primary) ?? screens[0];
    }

    private bool _windowPlaced; // windowed mode: only the first ApplySettings places the window
    private bool _restartingForSettings; // SETUP-triggered restart must not overwrite the new window settings

    private void ApplySettings()
    {
        TopMost = _settings.AlwaysOnTop;
        if (_settings.Windowed)
        {
            // Place once from saved bounds (or centered); after that the
            // window is the user's to move/resize — never stomp it.
            if (!_windowPlaced) { PlaceWindow(); _windowPlaced = true; }
            return;
        }
        var screen = TargetScreen(_settings);
        Bounds = screen.Bounds;
    }

    /// <summary>Restore the saved window bounds when they still land on a
    /// connected screen; otherwise center the window on the target display.</summary>
    private void PlaceWindow()
    {
        int w = Math.Max(_settings.WindowW, 640);
        int h = Math.Max(_settings.WindowH, 360);
        var saved = new Rectangle(_settings.WindowX, _settings.WindowY, w, h);
        if (_settings.WindowX >= 0 && Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(saved)))
        {
            Bounds = saved;
            return;
        }
        var t = TargetScreen(_settings);
        Bounds = new Rectangle(
            t.Bounds.Left + (t.Bounds.Width - w) / 2,
            t.Bounds.Top + (t.Bounds.Height - h) / 2, w, h);
    }

    /// <summary>--layout-dump: write the form + every card's real geometry
    /// to %APPDATA%\XeneonDash\layout-dump.txt for remote diagnosis.</summary>
    private void WriteLayoutDump()
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"XeneonDash layout dump {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Form bounds={Bounds} client={ClientSize} deviceDpi={DeviceDpi} topMost={TopMost}");
            sb.AppendLine($"FontScale={Theme.FontScale:0.00} theme={Theme.Current.Id}");
            foreach (var s in Screen.AllScreens)
                sb.AppendLine($"Screen primary={s.Primary} bounds={s.Bounds} working={s.WorkingArea}");
            void Walk(Control parent)
            {
                foreach (Control c in parent.Controls)
                {
                    if (c is Card card) sb.Append(card.DumpLayout());
                    Walk(c);
                }
            }
            Walk(this);
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XeneonDash");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "layout-dump.txt");
            File.WriteAllText(path, sb.ToString());
            MessageBox.Show($"Layout dump written to:\n{path}", "XeneonDash", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        catch { }
    }

    private void ShowPage(int i)
    {
        _pageIndex = i;
        for (int k = 0; k < _pages.Length; k++) _pages[k].Visible = k == i;
        for (int k = 0; k < _nav.Count; k++) _nav[k].Active = k == i;
        _top.Invalidate();
    }

    private void OpenSettings()
    {
        // Drop TopMost while the dialog is up: a modal dialog opened from a
        // fullscreen TopMost window can end up hidden behind it, freezing the app.
        bool wasTopMost = TopMost;
        TopMost = false;
        try
        {
            using var dlg = new SettingsForm(_settings) { TopMost = true };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _settings.Save();
                _csv.Enabled = _settings.CsvLogging;
                if (dlg.NeedsRestart)
                {
                    // Theme, text size, and window mode/size are applied at
                    // startup; restart to pick them up. Flag the close so
                    // the old window bounds don't stomp the new settings.
                    _restartingForSettings = true;
                    Application.Restart();
                    return;
                }
                _timer.Interval = Math.Clamp(_settings.RefreshMs, 250, 10000);
                ApplySettings();
            }
        }
        finally
        {
            TopMost = wasTopMost;
        }
    }

    private void Tick()
    {
        Snapshot snap;
        try { snap = _provider.TakeSnapshot(); }
        catch { return; }

        var now = DateTime.Now;
        _clockText = now.ToString("h:mm:ss tt");
        _dateText = now.ToString("dddd, MMMM d");
        _top.Invalidate();

        ((OverviewPage)_pages[0]).Update(snap);
        ((CpuPage)_pages[1]).Update(snap);
        ((GpuPage)_pages[2]).Update(snap);
        ((SystemPage)_pages[3]).Update(snap);

        _history.Push(snap);
        _session.Record(snap);
        _csv.MaybeLog(snap);
        ((HistoryPage)_pages[4]).Update(_history, snap, _csv.Enabled);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // Remember where the window lived so windowed mode reopens there.
        // Skipped during a SETUP restart: SETUP already saved the window
        // size the user picked; the closing window still wears the old
        // bounds and would stomp the choice.
        if (_settings.Windowed && !_restartingForSettings)
        {
            var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            if (b.Width >= 640 && b.Height >= 360)
            {
                _settings.WindowX = b.X;
                _settings.WindowY = b.Y;
                _settings.WindowW = b.Width;
                _settings.WindowH = b.Height;
                _settings.Save();
            }
        }
        _timer.Stop();
        _csv.Dispose();
        _provider.Dispose();
        base.OnFormClosed(e);
    }
}
