using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace XeneonDash;

public sealed class OverviewPage : UserControl
{
    private readonly Gauge _cpuGauge = new() { Caption = "CPU TEMP", Unit = "°C", Max = 100, Height = Theme.ScaleH(235), Dock = DockStyle.Top };
    private readonly Bar _cpuLoad = new() { Label = "LOAD" };
    private readonly StatRow _cpuClock = new() { Label = "Clock" };
    private readonly StatRow _cpuPower = new() { Label = "Package power" };
    private readonly Label _cpuName = new();

    private readonly Gauge _gpuGauge = new() { Caption = "GPU TEMP", Unit = "°C", Max = 100, Height = Theme.ScaleH(235), Dock = DockStyle.Top };
    private readonly Bar _gpuLoad = new() { Label = "LOAD", Fill = Theme.Cyan };
    private readonly Bar _gpuVram = new() { Label = "VRAM", Fill = Theme.Purple };
    private readonly StatRow _gpuFan = new() { Label = "Fan" };
    private readonly Label _gpuName = new();

    private readonly Bar _ramBar = new() { Label = "MEMORY", Fill = Theme.Green };
    private readonly StatRow[] _driveRows = new StatRow[4];
    private readonly StatRow[] _boardRows = new StatRow[3];

    private readonly Sparkline _down = new() { Label = "DOWNLOAD", Line = Theme.Cyan, Height = Theme.ScaleH(130) };
    private readonly Sparkline _up = new() { Label = "UPLOAD", Line = Theme.Accent, Height = Theme.ScaleH(130) };
    private readonly StatRow _netTotal = new() { Label = "Session data" };
    private readonly StatRow _uptime = new() { Label = "Uptime" };

    public OverviewPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        InitNameLabel(_cpuName);
        InitNameLabel(_gpuName);
        // Names of hardware (drives, board sensors) read as titles: full
        // brightness, not the dim metric-label color.
        _ramBar.LabelColor = Theme.Text;
        for (int i = 0; i < _driveRows.Length; i++) _driveRows[i] = new StatRow { LabelColor = Theme.Text };
        for (int i = 0; i < _boardRows.Length; i++) _boardRows[i] = new StatRow { LabelColor = Theme.Text };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Theme.Bg,
        };
        for (int i = 0; i < 4; i++)
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var cpuCard = new Card("CPU") { Dock = DockStyle.Fill };
        cpuCard.Controls.Add(_cpuPower);
        cpuCard.Controls.Add(_cpuClock);
        cpuCard.Controls.Add(_cpuLoad);
        cpuCard.Controls.Add(_cpuGauge);
        cpuCard.Controls.Add(_cpuName);

        var gpuCard = new Card("GPU") { Dock = DockStyle.Fill };
        gpuCard.Controls.Add(_gpuFan);
        gpuCard.Controls.Add(_gpuVram);
        gpuCard.Controls.Add(_gpuLoad);
        gpuCard.Controls.Add(_gpuGauge);
        gpuCard.Controls.Add(_gpuName);

        var memCard = new Card("Memory & storage") { Dock = DockStyle.Fill };
        for (int i = _boardRows.Length - 1; i >= 0; i--) memCard.Controls.Add(_boardRows[i]);
        for (int i = _driveRows.Length - 1; i >= 0; i--) memCard.Controls.Add(_driveRows[i]);
        memCard.Controls.Add(_ramBar);

        var netCard = new Card("Network & system") { Dock = DockStyle.Fill };
        netCard.Controls.Add(_uptime);
        netCard.Controls.Add(_netTotal);
        netCard.Controls.Add(_up);
        netCard.Controls.Add(_down);

        grid.Controls.Add(cpuCard, 0, 0);
        grid.Controls.Add(gpuCard, 1, 0);
        grid.Controls.Add(memCard, 2, 0);
        grid.Controls.Add(netCard, 3, 0);
        Controls.Add(grid);

        _cpuClock.TrackSession("cpu.clock", Format.ClockRange);
        _cpuPower.TrackSession("cpu.power", v => Format.Watts(v));
        for (int i = 0; i < _driveRows.Length; i++)
            _driveRows[i].TrackSession($"drive{i}.temp", v => Format.Temp(v));
    }

    private static void InitNameLabel(Label l)
    {
        l.Font = Theme.SmallFont;
        l.ForeColor = Theme.Dim;
        l.AutoEllipsis = true;
        l.Height = Theme.ScaleH(22);
        l.Dock = DockStyle.Top;
    }

    public void Update(Snapshot s)
    {
        _cpuName.Text = s.Cpu.Name;
        _cpuGauge.Value = s.Cpu.PackageTemp;
        _cpuLoad.Percent = s.Cpu.Load ?? 0;
        _cpuLoad.ValueText = Format.Percent(s.Cpu.Load);
        _cpuClock.ValueText = Format.Clock(s.Cpu.AvgClock);
        _cpuPower.ValueText = Format.Watts(s.Cpu.Power);
        _cpuClock.RefreshStats();
        _cpuPower.RefreshStats();

        var gpu = s.Gpus.FirstOrDefault();
        if (gpu == null)
        {
            _gpuName.Text = "No GPU sensors found";
            _gpuGauge.Value = null;
            _gpuLoad.Percent = 0; _gpuLoad.ValueText = "—";
            _gpuVram.Percent = 0; _gpuVram.ValueText = "—";
            _gpuFan.ValueText = "—";
        }
        else
        {
            _gpuName.Text = gpu.Name;
            _gpuGauge.Value = gpu.Temp;
            _gpuLoad.Percent = gpu.Load ?? 0;
            _gpuLoad.ValueText = Format.Percent(gpu.Load);
            if (gpu.MemTotalGB is float total && total > 0)
            {
                _gpuVram.Percent = (gpu.MemUsedGB ?? 0) / total * 100;
                _gpuVram.ValueText = $"{gpu.MemUsedGB.GetValueOrDefault():0.0} / {total:0.0} GB";
            }
            else { _gpuVram.Percent = 0; _gpuVram.ValueText = "—"; }
            _gpuFan.ValueText = !Format.Bad(gpu.FanRpm) ? Format.Rpm(gpu.FanRpm) : Format.Percent(gpu.FanPercent);
        }

        _ramBar.Percent = s.Memory.LoadPercent ?? 0;
        _ramBar.ValueText = !Format.Bad(s.Memory.UsedGB) && !Format.Bad(s.Memory.TotalGB)
            ? $"{s.Memory.UsedGB.Value:0.0} / {s.Memory.TotalGB.Value:0.0} GB" : "—";

        for (int i = 0; i < _driveRows.Length; i++)
        {
            if (i < s.Drives.Count)
            {
                var d = s.Drives[i];
                _driveRows[i].Visible = true;
                _driveRows[i].Label = d.Name;
                _driveRows[i].ValueText = Format.Temp(d.Temp);
                _driveRows[i].ValueColor = Theme.TempColor(d.Temp);
                _driveRows[i].RefreshStats();
                if (!Format.Bad(d.Life))
                    _driveRows[i].SubText += (_driveRows[i].SubText.Length > 0 ? "  ·  " : "")
                        + $"HEALTH {d.Life:0}%";
            }
            else _driveRows[i].Visible = false;
        }
        for (int i = 0; i < _boardRows.Length; i++)
        {
            if (i < s.BoardTemps.Count)
            {
                var t = s.BoardTemps[i];
                _boardRows[i].Visible = true;
                _boardRows[i].Label = t.Name;
                _boardRows[i].ValueText = Format.Temp(t.Value);
                _boardRows[i].ValueColor = Theme.TempColor(t.Value);
            }
            else _boardRows[i].Visible = false;
        }

        if (s.Network.DownBytes.HasValue) _down.Push(s.Network.DownBytes.Value);
        if (s.Network.UpBytes.HasValue) _up.Push(s.Network.UpBytes.Value);
        _down.ValueText = Format.BytesPerSec(s.Network.DownBytes);
        _up.ValueText = Format.BytesPerSec(s.Network.UpBytes);
        _netTotal.ValueText = !Format.Bad(s.Network.DownTotalGB)
            ? $"↓ {s.Network.DownTotalGB.Value:0.0} GB   ↑ {s.Network.UpTotalGB.GetValueOrDefault():0.0} GB" : "—";
        _uptime.ValueText = Format.Uptime(s.Uptime);
    }
}
