using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace XeneonDash;

/// <summary>Historical bar charts for the headline metrics, plus a session summary card.</summary>
public sealed class HistoryPage : UserControl
{
    private sealed class Metric
    {
        public HistoryBars Bars = new();
        public Func<HistoryData, HistoryBuffer> Select = _ => throw new InvalidOperationException();
    }

    private readonly List<Metric> _metrics = new();
    private readonly StatRow _uptime = new() { Label = "Uptime" };
    private readonly StatRow _samples = new() { Label = "History samples" };
    private readonly StatRow _csv = new() { Label = "CSV logging" };
    private readonly NavButton _reset = new("RESET STATS") { Dock = DockStyle.Top, Height = Theme.ScaleH(56) };

    /// <summary>Raised when the user taps RESET STATS (wired by MainForm to
    /// clear the session min/max/avg and the history buffers).</summary>
    public event Action? ResetRequested;

    public HistoryPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            BackColor = Theme.Bg,
        };
        for (int i = 0; i < 4; i++)
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        for (int i = 0; i < 2; i++)
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

        AddMetric(grid, 0, 0, "CPU TEMP", h => h.CpuTemp, Theme.Orange, v => Format.Temp(v));
        AddMetric(grid, 1, 0, "CPU LOAD", h => h.CpuLoad, Theme.Cyan, v => Format.Percent(v));
        AddMetric(grid, 2, 0, "GPU TEMP", h => h.GpuTemp, Theme.Red, v => Format.Temp(v));
        AddMetric(grid, 3, 0, "GPU LOAD", h => h.GpuLoad, Theme.Cyan, v => Format.Percent(v));
        AddMetric(grid, 0, 1, "MEMORY", h => h.MemPct, Theme.Green, v => Format.Percent(v));
        AddMetric(grid, 1, 1, "NET DOWN", h => h.NetDown, Theme.Cyan, v => Format.BytesPerSec(v), Format.BytesRange);
        AddMetric(grid, 2, 1, "NET UP", h => h.NetUp, Theme.Gold, v => Format.BytesPerSec(v), Format.BytesRange);

        var session = new Card("Session") { Dock = DockStyle.Fill };
        _reset.Click += (_, _) => ResetRequested?.Invoke();
        session.Controls.Add(_reset); // bottom of the stack (added first, docks last)
        session.Controls.Add(_csv);
        session.Controls.Add(_samples);
        session.Controls.Add(_uptime);
        grid.Controls.Add(session, 3, 1);

        Controls.Add(grid);
    }

    private void AddMetric(TableLayoutPanel grid, int col, int row, string title,
        Func<HistoryData, HistoryBuffer> select, Color fill, Func<float, string> fmt,
        Func<float, float, float, string>? fmtRange = null)
    {
        var card = new Card(title) { Dock = DockStyle.Fill };
        var bars = new HistoryBars { Fill = fill, FormatValue = fmt, FormatRange = fmtRange };
        card.Controls.Add(bars);
        grid.Controls.Add(card, col, row);
        _metrics.Add(new Metric { Bars = bars, Select = select });
    }

    public void Update(HistoryData h, Snapshot s, bool csvOn)
    {
        foreach (var m in _metrics)
        {
            m.Bars.Buffer = m.Select(h);
            m.Bars.Invalidate();
        }
        _uptime.ValueText = Format.Uptime(s.Uptime);
        _samples.ValueText = $"{h.Samples} samples";
        _csv.ValueText = csvOn ? "ON — Documents\\XeneonDash\\logs" : "OFF";
        _csv.ValueColor = csvOn ? Theme.Green : Theme.Dim;
    }
}
