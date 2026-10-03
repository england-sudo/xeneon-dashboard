using System;
using System.Drawing;
using System.Windows.Forms;

namespace XeneonDash;

public sealed class GpuPage : UserControl
{
    private readonly FlowLayoutPanel _flow = new();
    private int _builtFor = -1;

    public GpuPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        _flow.Dock = DockStyle.Fill;
        _flow.FlowDirection = FlowDirection.LeftToRight;
        _flow.WrapContents = false;
        _flow.BackColor = Theme.Bg;
        _flow.Resize += (_, _) => SizeCards();
        Controls.Add(_flow);
    }

    public void Update(Snapshot s)
    {
        if (s.Gpus.Count != _builtFor) Rebuild(s.Gpus.Count);
        for (int i = 0; i < s.Gpus.Count && i < _flow.Controls.Count; i++)
            ((GpuCard)_flow.Controls[i]).Update(s.Gpus[i]);
    }

    private void Rebuild(int n)
    {
        _flow.Controls.Clear();
        _builtFor = n;
        for (int i = 0; i < Math.Max(n, 1); i++)
            _flow.Controls.Add(new GpuCard());
        // Session stats are recorded for the primary GPU only.
        if (_flow.Controls.Count > 0)
            ((GpuCard)_flow.Controls[0]).EnableSessionStats();
        SizeCards();
    }

    private void SizeCards()
    {
        int count = Math.Max(_flow.Controls.Count, 1);
        int w = Math.Max(_flow.ClientSize.Width / count - 4, 200);
        int h = Math.Max(_flow.ClientSize.Height, 200);
        foreach (Control c in _flow.Controls)
        {
            c.Width = w;
            c.Height = h;
        }
    }

    private sealed class GpuCard : Card
    {
        private readonly Gauge _gauge = new() { Caption = "GPU TEMP", Unit = "°C", Max = 100, Height = Theme.ScaleH(220), Dock = DockStyle.Top };
        private readonly Bar _load = new() { Label = "CORE LOAD", Fill = Theme.Cyan };
        private readonly Bar _vram = new() { Label = "VRAM", Fill = Theme.Purple };
        private readonly StatRow _coreClock = new() { Label = "Core clock" };
        private readonly StatRow _memClock = new() { Label = "Memory clock" };
        private readonly StatRow _fan = new() { Label = "Fan" };
        private readonly StatRow _power = new() { Label = "Power" };
        private readonly StatRow _hotspot = new() { Label = "Hot spot" };

        public GpuCard() : base("")
        {
            // Two internal columns: gauge/bars left, ledger rows right. The
            // eight-child single column is ~560px into a 416px slot — no
            // shrink factor makes that respectable.
            var cols = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Card };
            cols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            cols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            cols.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            var gaugeCol = new LayoutPanel();
            gaugeCol.Controls.Add(_vram);
            gaugeCol.Controls.Add(_load);
            gaugeCol.Controls.Add(_gauge);
            // Gauge absorbs the column's leftover height at any size.
            gaugeCol.Resize += (_, _) =>
            {
                int rest = _load.Height + _load.Margin.Vertical
                         + _vram.Height + _vram.Margin.Vertical + _gauge.Margin.Vertical;
                _gauge.Height = Math.Max(Theme.ScaleH(60), gaugeCol.ClientSize.Height - rest);
            };
            var rowsCol = new LayoutPanel();
            rowsCol.Controls.Add(_hotspot);
            rowsCol.Controls.Add(_power);
            rowsCol.Controls.Add(_fan);
            rowsCol.Controls.Add(_memClock);
            rowsCol.Controls.Add(_coreClock);
            cols.Controls.Add(gaugeCol, 0, 0);
            cols.Controls.Add(rowsCol, 1, 0);
            Controls.Add(cols);
        }

        /// <summary>Session min/max/avg lines, for the primary GPU card only.</summary>
        public void EnableSessionStats()
        {
            _coreClock.TrackSession("gpu.clock", Format.ClockRange);
            _power.TrackSession("gpu.power", v => Format.Watts(v));
            _hotspot.TrackSession("gpu.hotspot", v => Format.Temp(v));
        }

        public void Update(GpuStats g)
        {
            Title = string.IsNullOrEmpty(g.Name) ? "GPU" : g.Name;
            _gauge.Value = g.Temp;
            _load.Percent = g.Load ?? 0;
            _load.ValueText = Format.Percent(g.Load);
            if (g.MemTotalGB is float total && total > 0)
            {
                _vram.Percent = (g.MemUsedGB ?? 0) / total * 100;
                _vram.ValueText = $"{g.MemUsedGB.GetValueOrDefault():0.0} / {total:0.0} GB";
            }
            else { _vram.Percent = 0; _vram.ValueText = "—"; }
            _coreClock.ValueText = Format.Clock(g.CoreClock);
            _memClock.ValueText = Format.Clock(g.MemClock);
            _fan.ValueText = !Format.Bad(g.FanRpm) ? Format.Rpm(g.FanRpm) : Format.Percent(g.FanPercent);
            _power.ValueText = Format.Watts(g.Power);
            _hotspot.ValueText = Format.Temp(g.HotSpot);
            _hotspot.ValueColor = Theme.TempColor(g.HotSpot);
            _coreClock.RefreshStats();
            _power.RefreshStats();
            _hotspot.RefreshStats();
        }
    }
}
