using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace XeneonDash;

public sealed class SystemPage : UserControl
{
    private readonly List<Bar> _fanBars = new();
    private readonly List<StatRow> _tempRows = new();
    private readonly StatRow _machine = new() { Label = "Machine" };
    private readonly StatRow _os = new() { Label = "OS" };
    private readonly StatRow _uptime = new() { Label = "Uptime" };
    private readonly StatRow _cpu = new() { Label = "CPU" };
    private readonly StatRow _gpu = new() { Label = "GPU" };
    private readonly StatRow _net = new() { Label = "Network session" };
    private readonly StatRow _pingGw = new() { Label = "Ping · Gateway" };
    private readonly StatRow _pingCf = new() { Label = "Ping · Cloudflare" };
    private readonly StatRow _pingGo = new() { Label = "Ping · Google" };
    private readonly NavButton _pingBtn = new("RUN PACKET TEST") { Dock = DockStyle.Top, Height = Theme.ScaleH(56) };
    private readonly PacketTest _ping = new();

    public SystemPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Theme.Bg };
        for (int i = 0; i < 3; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var fans = new Card("Fans") { Dock = DockStyle.Fill };
        for (int i = 0; i < 8; i++)
            _fanBars.Add(new Bar { Label = "", Fill = Theme.Cyan, Height = Theme.ScaleH(52), Margin = new Padding(0, 0, 0, Theme.Scale(4)) });
        for (int i = _fanBars.Count - 1; i >= 0; i--) fans.Controls.Add(_fanBars[i]);

        var temps = new Card("Temperatures") { Dock = DockStyle.Fill };
        for (int i = 0; i < 10; i++) _tempRows.Add(new StatRow());
        for (int i = _tempRows.Count - 1; i >= 0; i--) temps.Controls.Add(_tempRows[i]);

        var sys = new Card("System") { Dock = DockStyle.Fill };
        _pingBtn.Click += (_, _) => { if (_ping.Running) _ping.Stop(); else _ping.Start(); };
        sys.Controls.Add(_pingBtn);  // bottom of the stack (added first, docks last)
        sys.Controls.Add(_pingGo);
        sys.Controls.Add(_pingCf);
        sys.Controls.Add(_pingGw);
        sys.Controls.Add(_net);
        sys.Controls.Add(_gpu);
        sys.Controls.Add(_cpu);
        sys.Controls.Add(_uptime);
        sys.Controls.Add(_os);
        sys.Controls.Add(_machine);

        grid.Controls.Add(fans, 0, 0);
        grid.Controls.Add(temps, 1, 0);
        grid.Controls.Add(sys, 2, 0);
        Controls.Add(grid);
    }

    public void Update(Snapshot s)
    {
        for (int i = 0; i < _fanBars.Count; i++)
        {
            if (i < s.Fans.Count)
            {
                var f = s.Fans[i];
                _fanBars[i].Visible = true;
                _fanBars[i].Label = f.Name;
                _fanBars[i].Percent = f.Rpm.HasValue ? Math.Clamp(f.Rpm.Value / 2500f * 100f, 0, 100) : 0;
                _fanBars[i].ValueText = Format.Rpm(f.Rpm);
            }
            else _fanBars[i].Visible = false;
        }
        for (int i = 0; i < _tempRows.Count; i++)
        {
            if (i < s.BoardTemps.Count)
            {
                var t = s.BoardTemps[i];
                _tempRows[i].Visible = true;
                _tempRows[i].Label = t.Name;
                _tempRows[i].ValueText = Format.Temp(t.Value);
                _tempRows[i].ValueColor = Theme.TempColor(t.Value);
            }
            else _tempRows[i].Visible = false;
        }
        _machine.ValueText = Environment.MachineName;
        var v = Environment.OSVersion.Version;
        _os.ValueText = v.Build >= 22000 ? $"Windows 11 ({v})" : $"Windows 10 ({v})";
        _uptime.ValueText = Format.Uptime(s.Uptime);
        _cpu.ValueText = string.IsNullOrEmpty(s.Cpu.Name) ? "—" : s.Cpu.Name;
        _gpu.ValueText = s.Gpus.Count > 0 ? s.Gpus[0].Name : "—";
        _net.ValueText = !Format.Bad(s.Network.DownTotalGB)
            ? $"↓ {s.Network.DownTotalGB.Value:0.0} GB   ↑ {s.Network.UpTotalGB.GetValueOrDefault():0.0} GB" : "—";
        UpdatePingRows();
    }

    private void UpdatePingRows()
    {
        _pingBtn.Text = _ping.Running ? "STOP TEST" : "RUN PACKET TEST";
        _pingGw.Label = _ping.GatewayHost.Length > 0 ? $"Ping · {_ping.GatewayHost}" : "Ping · Gateway";
        if (!_ping.Running && !_ping.Done)
        {
            _pingGw.ValueText = "—";
            _pingGw.ValueColor = Theme.DimText;
            _pingCf.ValueText = "—";
            _pingCf.ValueColor = Theme.DimText;
            _pingGo.ValueText = "—";
            _pingGo.ValueColor = Theme.DimText;
            return;
        }
        if (_ping.HasGateway) ShowTally(_pingGw, _ping.Gateway);
        else { _pingGw.ValueText = "no gateway found"; _pingGw.ValueColor = Theme.Orange; }
        ShowTally(_pingCf, _ping.Cloudflare);
        ShowTally(_pingGo, _ping.Google);
    }

    private static void ShowTally(StatRow row, PingTally t)
    {
        int sent = Math.Min(t.Sent, PacketTest.PingCount);
        if (t.Sent == 0) { row.ValueText = "pinging…"; row.ValueColor = Theme.DimText; return; }
        string state = t.Sent >= PacketTest.PingCount ? "" : " · pinging…";
        row.ValueText = $"{t.AvgMs:0} ms avg · {t.LossPct:0}% loss ({t.Received}/{sent}){state}";
        row.ValueColor = t.LossPct <= 0 ? Theme.Green : t.LossPct < 10 ? Theme.Orange : Theme.Red;
    }
}
