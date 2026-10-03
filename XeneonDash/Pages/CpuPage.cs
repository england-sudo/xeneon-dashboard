using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace XeneonDash;

public sealed class CpuPage : UserControl
{
    private readonly Gauge _gauge = new() { Caption = "PACKAGE TEMP", Unit = "°C", Max = 100, Height = Theme.ScaleH(225), Dock = DockStyle.Top };
    private readonly Bar _load = new() { Label = "TOTAL LOAD" };
    private readonly StatRow _clock = new() { Label = "Avg clock" };
    private readonly StatRow _maxClock = new() { Label = "Max clock" };
    private readonly StatRow _power = new() { Label = "Package power" };
    private readonly StatRow _voltage = new() { Label = "Core voltage" };
    private readonly Label _name = new();
    private readonly List<Bar> _coreBars = new();

    public CpuPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        _name.Font = Theme.SmallFont;
        _name.ForeColor = Theme.Dim;
        _name.AutoEllipsis = true;
        _name.Height = Theme.ScaleH(22);
        _name.Dock = DockStyle.Top;

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Bg };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70F));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var left = new Card("CPU") { Dock = DockStyle.Fill };
        // Two internal columns: gauge stack left, stat rows right. A single
        // docked column of all seven children is ~510px into a 416px slot at
        // the design surface — it only ever "fit" via the shrink engine.
        var cols = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Card };
        cols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52F));
        cols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48F));
        cols.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        var gaugeCol = new LayoutPanel();
        gaugeCol.Controls.Add(_load);
        gaugeCol.Controls.Add(_gauge);
        gaugeCol.Controls.Add(_name);
        // Gauge takes whatever column height the bars/name leave, at any
        // screen size or text scale — no fixed design height to outgrow.
        gaugeCol.Resize += (_, _) =>
        {
            int rest = _name.Height + _name.Margin.Vertical
                     + _load.Height + _load.Margin.Vertical + _gauge.Margin.Vertical;
            _gauge.Height = Math.Max(Theme.ScaleH(60), gaugeCol.ClientSize.Height - rest);
        };
        var rowsCol = new LayoutPanel();
        rowsCol.Controls.Add(_voltage);
        rowsCol.Controls.Add(_power);
        rowsCol.Controls.Add(_maxClock);
        rowsCol.Controls.Add(_clock);
        cols.Controls.Add(gaugeCol, 0, 0);
        cols.Controls.Add(rowsCol, 1, 0);
        left.Controls.Add(cols);

        var right = new Card("Per-core load") { Dock = DockStyle.Fill };
        var cores = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 8, BackColor = Theme.Card };
        for (int i = 0; i < 4; i++) cores.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        for (int i = 0; i < 8; i++) cores.RowStyles.Add(new RowStyle(SizeType.Percent, 12.5F));
        for (int i = 0; i < 32; i++)
        {
            var b = new Bar
            {
                Label = $"C{i + 1}",
                Fill = Theme.Cyan,
                Dock = DockStyle.Fill,
                Margin = new Padding(Theme.Scale(10), Theme.Scale(3), Theme.Scale(10), Theme.Scale(3)),
            };
            _coreBars.Add(b);
            cores.Controls.Add(b, i % 4, i / 4);
        }
        right.Controls.Add(cores);

        grid.Controls.Add(left, 0, 0);
        grid.Controls.Add(right, 1, 0);
        Controls.Add(grid);

        _clock.TrackSession("cpu.clock", Format.ClockRange);
        _power.TrackSession("cpu.power", v => Format.Watts(v));
    }

    public void Update(Snapshot s)
    {
        _name.Text = s.Cpu.Name;
        _gauge.Value = s.Cpu.PackageTemp;
        _load.Percent = s.Cpu.Load ?? 0;
        _load.ValueText = Format.Percent(s.Cpu.Load);
        _clock.ValueText = Format.Clock(s.Cpu.AvgClock);
        _maxClock.ValueText = Format.Clock(s.Cpu.MaxClock);
        _power.ValueText = Format.Watts(s.Cpu.Power);
        _voltage.ValueText = Format.Bad(s.Cpu.Voltage) ? "—" : $"{s.Cpu.Voltage.Value:0.000} V";
        _clock.RefreshStats();
        _power.RefreshStats();

        for (int i = 0; i < _coreBars.Count; i++)
        {
            if (i < s.Cpu.Cores.Count)
            {
                var (name, load) = s.Cpu.Cores[i];
                _coreBars[i].Visible = true;
                _coreBars[i].Label = name;
                _coreBars[i].Percent = load ?? 0;
                _coreBars[i].ValueText = Format.Percent(load);
            }
            else _coreBars[i].Visible = false;
        }
    }
}
