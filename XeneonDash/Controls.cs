using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace XeneonDash;

/// <summary>Themed panel: chamfered body, theme frame + title treatment.</summary>
public class Card : Panel
{
    private string _title = "";
    public string Title
    {
        get => _title;
        set { _title = value; Invalidate(); }
    }

    // Scrollable body below the painted title zone. A private Panel subclass
    // so MainForm.HookBackgrounds (which targets plain Panels) leaves it alone.
    private sealed class BodyPanel : Panel
    {
        public BodyPanel()
        {
            Dock = DockStyle.Fill;
            BackColor = Color.Transparent;
            Padding = new Padding(Theme.Scale(22), Theme.Scale(6), Theme.Scale(22), Theme.Scale(18));
            DoubleBuffered = true;
        }
    }

    private readonly BodyPanel _view = new();
    // Design heights of the top-docked body children, captured on first layout.
    // Lets the card shrink its graphical children to fit (or scroll) when the
    // content is taller than the card — e.g. large text-size setting.
    private readonly Dictionary<Control, int> _designH = new();
    private bool _fitting;

    public Card() : this("") { }

    public Card(string title)
    {
        _title = title;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;
        // Title zone stays card surface (painted, pinned); the body fills the rest.
        Padding = new Padding(0, Theme.Scale(Theme.Current.TitleReserve), 0, 0);
        Margin = new Padding(Theme.Scale(8));
        BackColor = Theme.Bg;
        Controls.Add(_view);
    }

    /// <summary>Pages add content to card.Controls as before; it is reparented
    /// into the scrollable body so the painted title/frame stay pinned.</summary>
    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (e.Control != _view && e.Control.Parent == this)
            _view.Controls.Add(e.Control);
    }

    /// <summary>Minimum height when shrinking, or -1 when the child must keep
    /// its full height (text rows — shrinking them would clip the text).</summary>
    private static int MinH(Control c) => c switch
    {
        Gauge => Theme.ScaleH(110),
        Bar => Theme.ScaleH(40),
        Sparkline => Theme.ScaleH(70),
        HistoryBars => Theme.ScaleH(70),
        _ => -1,
    };

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_fitting || IsDisposed) return;
        foreach (Control c in _view.Controls)
        {
            if (c.Dock != DockStyle.Top) continue;
            // Design heights are captured once, before any fit-shrink/grow
            // touches the child. After a display-scaling change the cache is
            // dropped (see DpiChanged in the ctor) so stale sizes never stick.
            if (!_designH.ContainsKey(c))
                _designH[c] = c.Height;
        }
        FitContent();
    }

    // The avail height at which content was last shrunk, or -1. Growing back
    // to design heights is only allowed once the card offers MORE room than
    // that — stops grow/shrink oscillation across layout passes.
    private int _shrunkAtAvail = -1;

    private void FitContent()
    {
        int avail = _view.ClientSize.Height - _view.Padding.Vertical;
        if (avail <= 0) return;
        var kids = _designH.Keys.Where(k => !k.IsDisposed && k.Visible).ToList();
        if (kids.Count == 0) return;
        // Flex children are measured at their captured design height; fixed
        // children (stat rows, labels) use their live height because rows can
        // grow at runtime (e.g. when the session min/max/avg line appears).
        // Docked children also consume their vertical margins — leaving those
        // out undercounts every stack and clips the last row.
        int Consumed(Control k) => (MinH(k) > 0 ? _designH[k] : k.Height) + k.Margin.Vertical;
        int desired = kids.Sum(Consumed);
        // Ground truth from the last layout pass: the model above can still
        // be wrong at runtime (margins, DPI inflation mid-flight), so check
        // where the stack actually ENDS, not just where it should.
        int usedMeasured = 0;
        foreach (var k in kids)
            usedMeasured = Math.Max(usedMeasured, k.Bottom + k.Margin.Bottom - _view.Padding.Top);
        bool measuredFits = usedMeasured <= avail;

        _fitting = true;
        try
        {
            bool canRestore = _shrunkAtAvail < 0 || avail > _shrunkAtAvail;
            if (desired <= avail && measuredFits && canRestore)
            {
                // Room to spare (taller display than the design surface):
                // grow the graphical children into the space, capped at
                // 145% of design, instead of leaving a dead band at the
                // bottom of the card.
                int extra = avail - desired;
                var flexKids = kids.Where(k => MinH(k) > 0).ToList();
                int flexConsumed = flexKids.Sum(Consumed);
                foreach (var k in kids)
                {
                    int want = MinH(k) > 0 ? _designH[k] : k.Height;
                    if (MinH(k) > 0 && extra > 0 && flexConsumed > 0)
                    {
                        int share = (int)Math.Round((double)extra * Consumed(k) / flexConsumed);
                        want = Math.Min((int)(_designH[k] * 1.45), want + share);
                    }
                    if (k.Height != want) k.Height = want;
                }
                _shrunkAtAvail = -1;
                if (_view.AutoScroll) _view.AutoScroll = false;
                return;
            }
            var flex = kids.Where(k => MinH(k) > 0).ToList();
            var fixedKids = kids.Where(k => MinH(k) <= 0).ToList();
            int fixedTotal = fixedKids.Sum(k => k.Height + k.Margin.Vertical);
            _shrunkAtAvail = avail;
            if (flex.Count == 0 || fixedTotal >= avail)
            {
                // Nothing shrinkable, or even the fixed rows overflow: scroll.
                foreach (var k in flex) k.Height = Math.Min(_designH[k], MinH(k));
                _view.AutoScroll = true;
                return;
            }
            double f;
            if (desired > avail)
            {
                int flexRoom = Math.Max(0, avail - fixedTotal - flex.Sum(k => k.Margin.Vertical));
                f = (double)flexRoom / flex.Sum(k => _designH[k]);
            }
            else
            {
                // Model says it fits but the rendered stack overflows:
                // shrink by the measured overflow ratio instead.
                f = usedMeasured > 0 ? (double)avail / usedMeasured : 1.0;
            }
            f = Math.Clamp(f, 0, 1);
            foreach (var k in flex)
                k.Height = Math.Max(MinH(k), (int)(_designH[k] * f));
            // Self-correction across layout passes: the predictive model
            // can still miss on real hardware (runtime DPI scaling warps
            // margins/padding in ways the sums don't predict). Each pass
            // re-measures the stack and trims by the overflow ratio —
            // monotonic shrink, so this converges instead of trusting one
            // perfect calculation.
            if (!measuredFits && usedMeasured > 0)
            {
                double mf = (double)avail / usedMeasured;
                if (mf < 1)
                    foreach (var k in flex)
                        k.Height = Math.Max(MinH(k), (int)(k.Height * mf));
            }
            _view.AutoScroll = fixedTotal + flex.Sum(k => k.Height + k.Margin.Vertical) > avail
                || (!measuredFits && flex.All(k => k.Height <= MinH(k)));
        }
        finally { _fitting = false; }
    }

    /// <summary>Drop captured design heights (display-scaling change
    /// inflated the children behind our back; re-read them next layout).</summary>
    internal void ResetDesignCache()
    {
        _designH.Clear();
        _shrunkAtAvail = -1;
    }

    /// <summary>Real on-screen geometry of this card for --layout-dump:
    /// the numbers the fitter sees, dumped when remote diagnosis is needed.</summary>
    internal string DumpLayout()
    {
        int avail = _view.ClientSize.Height - _view.Padding.Vertical;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"CARD '{Title}' size={Width}x{Height} body={_view.ClientSize.Width}x{_view.ClientSize.Height} avail={avail} availW={_view.ClientSize.Width - _view.Padding.Horizontal} autoScroll={_view.AutoScroll} titlePad={Padding.Top}");
        foreach (Control k in _view.Controls)
            sb.AppendLine($"  {k.GetType().Name,-12} dock={k.Dock} vis={k.Visible} top={k.Top} h={k.Height} margV={k.Margin.Vertical} bot={k.Bottom + k.Margin.Bottom}");
        return sb.ToString();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Bg);
        var rect = new Rectangle(0, 3, Width - 1, Height - 4);
        using var path = Theme.CardPath(rect);
        using var bg = new SolidBrush(Theme.Card);
        g.FillPath(bg, path);
        Theme.PaintFrame(g, rect);
        if (!string.IsNullOrEmpty(_title))
            Theme.PaintTitle(g, _title.ToUpperInvariant(), rect);
    }
}

/// <summary>Gold filigree divider strip.</summary>
public sealed class Divider : Control
{
    public Color FillColor { get; set; } = Theme.Bg;

    public Divider()
    {
        Height = Theme.Scale(22);
        Dock = DockStyle.Top;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(FillColor);
        Theme.PaintDivider(g, 40, Height / 2, Width - 80);
    }
}

/// <summary>Plain layout container for use inside cards. A distinct type so
/// MainForm.HookBackgrounds — which paints the themed backdrop onto plain
/// Panels and TableLayoutPanels — leaves card interiors flat.</summary>
public sealed class LayoutPanel : Panel
{
    public LayoutPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Card;
        Margin = new Padding(0);
    }
}

/// <summary>Single label/value ledger row.</summary>
public sealed class StatRow : Control
{
    private string _label = "";
    private string _value = "—";
    private Color _valueColor = Theme.Text;

    public string Label
    {
        get => _label;
        set { _label = value; Invalidate(); }
    }

    private Color _labelColor = Theme.Dim;
    /// <summary>Label color. Rows whose label is a hardware NAME (drive,
    /// board sensor) opt into full Text brightness — Dim brown-on-brown
    /// made them hard to read.</summary>
    public Color LabelColor
    {
        get => _labelColor;
        set { _labelColor = value; Invalidate(); }
    }

    public string ValueText
    {
        get => _value;
        set { _value = value; Invalidate(); }
    }

    public Color ValueColor
    {
        get => _valueColor;
        set { _valueColor = value; Invalidate(); }
    }

    private string _sub = "";

    /// <summary>Optional dim second line. The row grows to fit it when set.</summary>
    public string SubText
    {
        get => _sub;
        set
        {
            if (_sub == value) return;
            _sub = value;
            Height = Theme.ScaleH(string.IsNullOrEmpty(_sub) ? 32 : 48);
            Invalidate();
        }
    }

    /// <summary>Session stats source, assigned once by MainForm.</summary>
    public static SessionStats? Session;

    private string _statsKey = "";
    private Func<float, string>? _statsFmt = v => $"{v:0.0}";
    private Func<float, float, float, string>? _statsFmt3;

    /// <summary>Show session MIN / MAX / AVG under this row, formatted by <paramref name="fmt"/>.</summary>
    public void TrackSession(string key, Func<float, string> fmt)
    {
        _statsKey = key;
        _statsFmt = fmt;
        _statsFmt3 = null;
        RefreshStats();
    }

    /// <summary>Uniform-unit variant: the formatter receives (min, max, avg) and
    /// renders the whole line in one unit (see Format.ClockRange).</summary>
    public void TrackSession(string key, Func<float, float, float, string> fmt)
    {
        _statsKey = key;
        _statsFmt = null;
        _statsFmt3 = fmt;
        RefreshStats();
    }

    /// <summary>Recompute the min/max/avg line from the session stats.</summary>
    public void RefreshStats()
    {
        if (string.IsNullOrEmpty(_statsKey)) return;
        if (_statsFmt3 != null) SubText = Session?.Summary(_statsKey, _statsFmt3) ?? "";
        else if (_statsFmt != null) SubText = Session?.Summary(_statsKey, _statsFmt) ?? "";
    }

    public StatRow()
    {
        Height = Theme.ScaleH(32);
        Dock = DockStyle.Top;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Card);
        bool two = !string.IsNullOrEmpty(_sub);
        int y = two ? 2 : 5;
        TextRenderer.DrawText(g, _label, Theme.LabelFont, new Point(0, y), _labelColor,
            TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        var sz = TextRenderer.MeasureText(g, _value, Theme.LabelFont);
        TextRenderer.DrawText(g, _value, Theme.LabelFont, new Point(Width - sz.Width, y), _valueColor,
            TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        if (two)
            TextRenderer.DrawText(g, _sub, Theme.SmallFont, new Point(0, Height - 19), Theme.DimText,
                TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        using var pen = new Pen(Color.FromArgb(38, Theme.BorderDim), 1f);
        g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }
}

/// <summary>Label + value + angular arcane progress bar.</summary>
public sealed class Bar : Control
{
    private string _label = "";
    private string _valueText = "";
    private float _percent;
    private Color _fill = Theme.Accent;

    public string Label
    {
        get => _label;
        set { _label = value; Invalidate(); }
    }

    private Color _labelColor = Theme.Dim;
    /// <summary>Label color; see StatRow.LabelColor.</summary>
    public Color LabelColor
    {
        get => _labelColor;
        set { _labelColor = value; Invalidate(); }
    }

    public string ValueText
    {
        get => _valueText;
        set { _valueText = value; Invalidate(); }
    }

    public float Percent
    {
        get => _percent;
        set { _percent = float.IsNaN(value) || float.IsInfinity(value) ? 0 : Math.Clamp(value, 0, 100); Invalidate(); }
    }

    public Color Fill
    {
        get => _fill;
        set { _fill = value; Invalidate(); }
    }

    public Bar()
    {
        Height = Theme.ScaleH(62);
        Dock = DockStyle.Top;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Margin = new Padding(0, 0, 0, Theme.Scale(6));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Card);
        TextRenderer.DrawText(g, _label, Theme.LabelFont, new Point(0, 0), _labelColor,
            TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        if (!string.IsNullOrEmpty(_valueText))
        {
            var sz = TextRenderer.MeasureText(g, _valueText, Theme.LabelFont);
            TextRenderer.DrawText(g, _valueText, Theme.LabelFont, new Point(Width - sz.Width, 0), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        }
        // When the card auto-fit shrinks the bar, pull the track up so it never clips.
        int trackY = Math.Min(30, Height - 17);
        Theme.PaintBar(g, new Rectangle(0, trackY, Width, 15), _percent / 100f, _fill);
    }
}

/// <summary>Arcane rune-ring gauge: tick circle, glowing progress arc, engraved number.</summary>
public sealed class Gauge : Control
{
    private float? _value;
    public float? Value
    {
        get => _value;
        set { _value = Format.Bad(value) ? null : value; Invalidate(); }
    }

    public float Min { get; set; } = 0;
    public float Max { get; set; } = 100;
    public string Unit { get; set; } = "°C";
    public string Caption { get; set; } = "";
    public Func<float?, Color> ColorFor { get; set; } = Theme.TempColor;

    public Gauge()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        float frac = _value.HasValue ? Math.Clamp((_value.Value - Min) / (Max - Min), 0, 1) : 0;
        string txt = _value.HasValue ? $"{_value.Value:0}" : "—";
        Theme.PaintGauge(e.Graphics, new Rectangle(0, 0, Width, Height),
            frac, txt, Unit, Caption, ColorFor(_value));
    }
}

/// <summary>Scrolling history line, e.g. network throughput.</summary>
public sealed class Sparkline : Control
{
    private readonly Queue<float> _hist = new();
    private string _label = "";
    private string _valueText = "";
    private Color _line = Theme.Cyan;

    public string Label
    {
        get => _label;
        set { _label = value; Invalidate(); }
    }

    public string ValueText
    {
        get => _valueText;
        set { _valueText = value; Invalidate(); }
    }

    public Color Line
    {
        get => _line;
        set { _line = value; Invalidate(); }
    }

    public int Capacity { get; set; } = 90;

    public Sparkline()
    {
        Height = Theme.ScaleH(96);
        Dock = DockStyle.Top;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public void Push(float v)
    {
        // LHM reports NaN for unreadable sensors; a single NaN in the
        // history poisons Max() and the whole graph, so drop it.
        if (float.IsNaN(v) || float.IsInfinity(v)) return;
        if (_hist.Count >= Capacity) _hist.Dequeue();
        _hist.Enqueue(v);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Card);
        TextRenderer.DrawText(g, _label, Theme.LabelFont, new Point(0, 2), Theme.Dim,
            TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        if (!string.IsNullOrEmpty(_valueText))
        {
            var sz = TextRenderer.MeasureText(g, _valueText, Theme.MedNumberFont);
            TextRenderer.DrawText(g, _valueText, Theme.MedNumberFont, new Point(Width - sz.Width, -4), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        }
        if (_hist.Count < 2) return;
        float max = Math.Max(_hist.Max(), 1f);
        var pts = _hist.Select((v, i) => new PointF(
            4 + i * (Width - 8f) / (Capacity - 1),
            Height - 8 - (v / max) * (Height - 52))).ToArray();
        var fillPts = pts.Concat(new[]
        {
            new PointF(pts[pts.Length - 1].X, Height - 4),
            new PointF(pts[0].X, Height - 4),
        }).ToArray();
        using (var brush = new SolidBrush(Color.FromArgb(40, _line)))
            g.FillPolygon(brush, fillPts);
        using var pen = new Pen(_line, 2.5f);
        g.DrawLines(pen, pts);
    }
}

/// <summary>Big touch-friendly bottom nav button: chamfered gold plate when active.</summary>
public sealed class NavButton : Button
{
    private bool _active;
    public bool Active
    {
        get => _active;
        set { _active = value; Invalidate(); }
    }

    public NavButton(string text)
    {
        Text = text;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        FlatAppearance.MouseDownBackColor = Color.Transparent;
        FlatAppearance.MouseOverBackColor = Color.Transparent;
        BackColor = Color.Transparent;
        Font = Theme.NavFont;
        Cursor = Cursors.Hand;
        TabStop = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Bg);
        var rect = new Rectangle(8, 12, Width - 16, Height - 24);
        Theme.PaintNav(g, rect, Text, _active);
    }
}

/// <summary>Historical bar chart: oldest sample left, newest right, with the
/// current value up top and session min/max/avg along the bottom.</summary>
public sealed class HistoryBars : Control
{
    private HistoryBuffer? _buffer;
    private Color _fill = Theme.Cyan;
    private Func<float, string> _fmt = v => $"{v:0.0}";
    private readonly List<float> _tmp = new();

    public HistoryBuffer? Buffer
    {
        get => _buffer;
        set { _buffer = value; Invalidate(); }
    }

    public Color Fill
    {
        get => _fill;
        set { _fill = value; Invalidate(); }
    }

    public Func<float, string> FormatValue
    {
        get => _fmt;
        set { _fmt = value; Invalidate(); }
    }

    /// <summary>Optional uniform-unit footer formatter: receives (min, max, avg)
    /// and renders the whole line in one unit (see Format.BytesRange).</summary>
    public Func<float, float, float, string>? FormatRange;

    public HistoryBars()
    {
        Height = Theme.ScaleH(150);
        Dock = DockStyle.Top;
        // NB: do NOT set BackColor = Transparent here — a plain Control throws
        // "Control does not support transparent background colors". The body is
        // flat Theme.Card, so just paint it opaquely like the sibling controls.
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Card);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        _tmp.Clear();
        _buffer?.CopyValidTo(_tmp);

        const TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        if (_tmp.Count == 0)
        {
            TextRenderer.DrawText(g, "collecting…", Theme.SmallFont, ClientRectangle, Theme.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | flags);
            return;
        }

        float min = _tmp[0], max = _tmp[0], sum = 0;
        foreach (var v in _tmp)
        {
            if (v < min) min = v;
            if (v > max) max = v;
            sum += v;
        }
        float avg = sum / _tmp.Count;
        float range = max - min;
        if (range <= 0) range = Math.Max(1, Math.Abs(max) * 0.1f);

        // Current value, top-right.
        string cur = _fmt(_buffer!.Latest);
        var curSz = TextRenderer.MeasureText(g, cur, Theme.MedNumberFont);
        TextRenderer.DrawText(g, cur, Theme.MedNumberFont,
            new Point(Width - curSz.Width - 4, 2), Theme.Text, flags);

        // Footer: min / max / avg + sample count.
        string foot = FormatRange != null
            ? FormatRange(min, max, avg)
            : $"MIN {_fmt(min)}  ·  MAX {_fmt(max)}  ·  AVG {_fmt(avg)}";
        TextRenderer.DrawText(g, foot, Theme.SmallFont, new Point(4, Height - 20), Theme.DimText, flags);
        string n = $"{_tmp.Count} samples";
        var nSz = TextRenderer.MeasureText(g, n, Theme.SmallFont);
        TextRenderer.DrawText(g, n, Theme.SmallFont, new Point(Width - nSz.Width - 4, Height - 20), Theme.DimText, flags);

        // Bars between header and footer.
        int top = 40, bottom = Height - 26;
        int bh = bottom - top;
        if (bh <= 8) return;
        int count = _tmp.Count;
        float slot = (float)(Width - 8) / count;
        float barW = Math.Max(2f, Math.Min(slot - 1f, 14f));
        using var brush = new SolidBrush(_fill);
        using var hotBrush = new SolidBrush(Theme.GoldHi);
        for (int i = 0; i < count; i++)
        {
            float x = 4 + i * slot + (slot - barW) / 2f;
            float h = Math.Max(2f, (_tmp[i] - min) / range * (bh - 2));
            var r = new RectangleF(x, bottom - h, barW, h);
            g.FillRectangle(i == count - 1 ? hotBrush : brush, r);
        }
    }
}

/// <summary>Owner-drawn ComboBox that stays dark when dropped down.</summary>
public sealed class ThemedCombo : ComboBox
{
    public ThemedCombo()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        DropDownStyle = ComboBoxStyle.DropDownList;
        BackColor = Theme.Card;
        ForeColor = Theme.Text;
        Font = Theme.LabelFont;
        ItemHeight = 30;
        FlatStyle = FlatStyle.Flat;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        e.DrawBackground();
        if (e.Index < 0) return;
        bool sel = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var bg = sel ? Theme.Border : Theme.Card;
        using var b = new SolidBrush(bg);
        e.Graphics.FillRectangle(b, e.Bounds);
        string text = Items[e.Index]?.ToString() ?? "";
        TextRenderer.DrawText(e.Graphics, text, Font,
            new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 20, e.Bounds.Height),
            sel ? Theme.GoldHi : Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

/// <summary>Custom-drawn toggle row (label + checkbox) that matches the theme.</summary>
public sealed class ToggleRow : Control
{
    private bool _checked;

    public bool Checked
    {
        get => _checked;
        set { _checked = value; Invalidate(); }
    }

    public ToggleRow()
    {
        Height = Theme.ScaleH(34);
        Font = Theme.LabelFont;
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnClick(EventArgs e)
    {
        Checked = !Checked;
        Focus();
        base.OnClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
        {
            Checked = !Checked;
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Bg);
        int box = 20;
        int y = (Height - box) / 2;
        var rect = new Rectangle(2, y, box, box);
        using var bg = new SolidBrush(_checked ? Theme.Gold : Theme.Card);
        g.FillRectangle(bg, rect);
        using var pen = new Pen(Focused ? Theme.GoldHi : Theme.Border, 2f);
        g.DrawRectangle(pen, rect);
        if (_checked)
        {
            using var cp = new Pen(Theme.Bg, 2.5f);
            g.DrawLines(cp, new[]
            {
                new Point(rect.Left + 4, rect.Top + 10),
                new Point(rect.Left + 9, rect.Top + 15),
                new Point(rect.Left + 16, rect.Top + 6),
            });
        }
        TextRenderer.DrawText(g, Text, Font, new Rectangle(rect.Right + 12, 0, Width - rect.Right - 12, Height),
            Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}
