using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;

namespace XeneonDash;

public sealed class CpuStats
{
    public string Name = "";
    public float? PackageTemp;
    public float? Load;
    public float? AvgClock;
    public float? MaxClock;
    public float? Power;
    public float? Voltage;
    public readonly List<(string Name, float? Load)> Cores = new();
}

public sealed class GpuStats
{
    public string Name = "";
    public float? Temp;
    public float? HotSpot;
    public float? Load;
    public float? CoreClock;
    public float? MemClock;
    public float? FanRpm;
    public float? FanPercent;
    public float? Power;
    public float? MemUsedGB;
    public float? MemTotalGB;
}

public sealed class MemStats
{
    public float? LoadPercent;
    public float? UsedGB;
    public float? TotalGB;
}

public sealed class DriveStats
{
    public string Name = "";
    public float? Temp;
    public float? Life; // SMART remaining life %, 0..100
}

public sealed class FanStats
{
    public string Name = "";
    public float? Rpm;
}

public sealed class TempStats
{
    public string Name = "";
    public float? Value;
}

public sealed class NetStats
{
    public string Name = "";
    public float? DownBytes;
    public float? UpBytes;
    public float? DownTotalGB;
    public float? UpTotalGB;
}

public sealed class NetCandidate
{
    public string Name = "";
    public float Down;
    public float Up;
    public float? DownTotalGB;
    public float? UpTotalGB;
}

public sealed class Snapshot
{
    public CpuStats Cpu = new();
    public readonly List<GpuStats> Gpus = new();
    public MemStats Memory = new();
    public readonly List<DriveStats> Drives = new();
    public readonly List<FanStats> Fans = new();
    public readonly List<TempStats> BoardTemps = new();
    public NetStats Network = new();
    public readonly List<NetCandidate> NetCandidates = new();
    public TimeSpan Uptime;
}

internal static class Format
{
    /// <summary>True when a sensor value is missing or garbage (LHM reports NaN for unreadable sensors).</summary>
    public static bool Bad(float? v) => !v.HasValue || float.IsNaN(v.Value) || float.IsInfinity(v.Value);

    public static string Percent(float? v) => Bad(v) ? "—" : $"{v.Value:0}%";

    public static string Rpm(float? v) => Bad(v) ? "—" : $"{v.Value:0} RPM";

    public static string BytesPerSec(float? bps)
    {
        if (Bad(bps)) return "—";
        double v = bps.Value;
        string[] units = { "B/s", "KB/s", "MB/s", "GB/s" };
        int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return $"{v:0.0} {units[i]}";
    }

    public static string Clock(float? mhz) =>
        Bad(mhz) ? "—" : (mhz.Value >= 1000 ? $"{mhz.Value / 1000f:0.00} GHz" : $"{mhz.Value:0} MHz");

    /// <summary>Min/max/avg in ONE unit (GHz when the max reaches 1 GHz, else MHz),
    /// so the stat line never mixes units like "210 MHz · 2.76 GHz".</summary>
    public static string ClockRange(float mn, float mx, float av) =>
        mx >= 1000
            ? $"MIN {mn / 1000f:0.00} · MAX {mx / 1000f:0.00} · AVG {av / 1000f:0.00} GHz"
            : $"MIN {mn:0} · MAX {mx:0} · AVG {av:0} MHz";

    /// <summary>Min/max/avg throughput in ONE unit, picked from the max value.</summary>
    public static string BytesRange(float mn, float mx, float av)
    {
        string[] units = { "B/s", "KB/s", "MB/s", "GB/s" };
        double v = Math.Max(0, mx);
        int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        double s = Math.Pow(1024, i);
        return $"MIN {mn / s:0.0} · MAX {mx / s:0.0} · AVG {av / s:0.0} {units[i]}";
    }

    /// <summary>Sensor value when inside the plausible range, else null. LHM emits
    /// garbage (0, negatives, 2x-doubled clocks) for sensors it can't really read;
    /// those must not reach the UI or the session stats.</summary>
    public static float? Sane(float? v, float lo, float hi) =>
        v.HasValue && !float.IsNaN(v.Value) && !float.IsInfinity(v.Value)
        && v.Value >= lo && v.Value <= hi ? v : null;

    public static string Temp(float? c) => Bad(c) ? "—" : $"{c.Value:0.0}°C";

    public static string Watts(float? w) => Bad(w) ? "—" : $"{w.Value:0} W";

    public static string Uptime(TimeSpan t)
    {
        if (t.TotalDays >= 1) return $"{(int)t.TotalDays}d {t.Hours}h {t.Minutes}m";
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
        return $"{t.Minutes}m {t.Seconds}s";
    }
}

/// <summary>
/// Reads hardware sensors via LibreHardwareMonitor. Never throws out of
/// TakeSnapshot — a missing sensor just shows "—" in the UI.
/// </summary>
public sealed class StatsProvider : IDisposable
{
    private readonly Computer _computer = new();
    private readonly bool _demo;
    private readonly Random _rng = new();
    private string? _netName; // sticky network adapter (see TakeSnapshot)
    private float _demoCpuT = 52;
    private float _demoGpuT = 60;
    private float _demoCpuLoad = 35;
    private float _demoGpuLoad = 45;

    public StatsProvider(bool demo)
    {
        _demo = demo;
        _computer.IsCpuEnabled = true;
        _computer.IsGpuEnabled = true;
        _computer.IsMemoryEnabled = true;
        _computer.IsMotherboardEnabled = true;
        _computer.IsNetworkEnabled = true;
        _computer.IsStorageEnabled = true;
        _computer.IsControllerEnabled = true;
        _computer.IsPsuEnabled = true;
        if (!demo)
        {
            try { _computer.Open(); }
            catch { /* sensors just won't be there */ }
        }
    }

    public Snapshot TakeSnapshot()
    {
        if (_demo) return DemoSnapshot();

        var snap = new Snapshot { Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64) };
        try
        {
            foreach (var hw in _computer.Hardware)
            {
                try
                {
                    hw.Update();
                    foreach (var sub in hw.SubHardware)
                    {
                        try { sub.Update(); } catch { }
                        Collect(sub, snap);
                    }
                    Collect(hw, snap);
                }
                catch { }
            }

            // Sticky adapter pick: stay on the adapter shown last tick unless it
            // vanished or another one is carrying 5x its traffic — otherwise two
            // busy adapters (Wi-Fi + dock, VPN + NIC) flip the display every tick.
            var best = snap.NetCandidates.OrderByDescending(n => n.Down + n.Up).FirstOrDefault();
            if (best != null && _netName != null && best.Name != _netName)
            {
                var cur = snap.NetCandidates.FirstOrDefault(n => n.Name == _netName);
                if (cur != null && best.Down + best.Up <= (cur.Down + cur.Up) * 5)
                    best = cur;
            }
            if (best != null)
            {
                _netName = best.Name;
                snap.Network.Name = best.Name;
                snap.Network.DownBytes = best.Down;
                snap.Network.UpBytes = best.Up;
                snap.Network.DownTotalGB = best.DownTotalGB;
                snap.Network.UpTotalGB = best.UpTotalGB;
            }

            // Prefer the discrete GPU first: on chips with an iGPU the
            // Overview page shows Gpus[0], which should be the real card.
            snap.Gpus.Sort((a, b) => (b.MemTotalGB ?? 0).CompareTo(a.MemTotalGB ?? 0));
        }
        catch { }
        return snap;
    }

    private static List<ISensor> SensorsOf(IHardware hw, SensorType type) =>
        hw.Sensors.Where(s => s.SensorType == type).ToList();

    private static ISensor? Find(IEnumerable<ISensor> sensors, params string[] contains)
    {
        var list = sensors.ToList();
        foreach (var c in contains)
        {
            var m = list.FirstOrDefault(s => s.Name.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0);
            if (m != null) return m;
        }
        return null;
    }

    private static string Shorten(string s, int max) =>
        string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max - 1) + "…");

    private void Collect(IHardware hw, Snapshot snap)
    {
        switch (hw.HardwareType)
        {
            case HardwareType.Cpu:
                CollectCpu(hw, snap);
                break;
            case HardwareType.GpuNvidia:
            case HardwareType.GpuAmd:
            case HardwareType.GpuIntel:
                snap.Gpus.Add(CollectGpu(hw));
                break;
            case HardwareType.Memory:
                CollectMemory(hw, snap);
                break;
            case HardwareType.Storage:
                CollectDrive(hw, snap);
                break;
            case HardwareType.Network:
                CollectNetwork(hw, snap);
                break;
            case HardwareType.Motherboard:
            case HardwareType.SuperIO:
            case HardwareType.EmbeddedController:
            case HardwareType.Cooler:
                CollectBoard(hw, snap);
                break;
            default:
                break;
        }
    }

    private void CollectCpu(IHardware hw, Snapshot snap)
    {
        if (!string.IsNullOrEmpty(snap.Cpu.Name)) return; // first CPU wins
        var c = snap.Cpu;
        c.Name = Shorten(hw.Name, 40);

        var temps = SensorsOf(hw, SensorType.Temperature);
        var loads = SensorsOf(hw, SensorType.Load);
        var clocks = SensorsOf(hw, SensorType.Clock);

        // A 0-ish package temp is the classic not-enough-rights / half-decoded
        // read (a running die is never at 0 °C), so distrust it and fall back
        // to averaging the per-core sensors, which decode independently.
        c.PackageTemp = Format.Sane(Find(temps, "Package", "Tctl", "Tdie")?.Value, 1, 160);
        if (Format.Bad(c.PackageTemp))
        {
            c.PackageTemp = null;
            var coreTemps = temps
                .Where(t => t.Name.IndexOf("Core #", StringComparison.OrdinalIgnoreCase) >= 0 && !Format.Bad(Format.Sane(t.Value, 1, 160)))
                .Select(t => t.Value!.Value).ToList();
            if (coreTemps.Count > 0) c.PackageTemp = coreTemps.Average();
        }

        c.Load = Format.Sane(Find(loads, "Total")?.Value, 0, 100);

        // Prefer LHM's own canonical average ("Cores (Average)") over re-averaging:
        // a loose "Core" name match also swept up the "(Effective)" variants and
        // double-counted everything. Discard physically implausible readings —
        // LHM's Zen 5 clock decode can report ~2x the real frequency.
        static bool PlausibleClock(float? v) => v.HasValue && v.Value > 0 && v.Value <= 8500;

        var avgSensor = clocks.FirstOrDefault(s => s.Name.Equals("Cores (Average)", StringComparison.OrdinalIgnoreCase));
        c.AvgClock = PlausibleClock(avgSensor?.Value) ? avgSensor!.Value : null;

        var perCore = clocks
            .Where(s => s.Name.StartsWith("Core #", StringComparison.OrdinalIgnoreCase)
                        && s.Name.IndexOf("Effective", StringComparison.OrdinalIgnoreCase) < 0
                        && PlausibleClock(s.Value))
            .Select(s => s.Value!.Value).ToList();
        if (!c.AvgClock.HasValue && perCore.Count > 0) c.AvgClock = perCore.Average();
        if (perCore.Count > 0) c.MaxClock = perCore.Max();

        c.Power = Format.Sane(Find(SensorsOf(hw, SensorType.Power), "Package", "Core")?.Value, 0, 2000);
        c.Voltage = Format.Sane(Find(SensorsOf(hw, SensorType.Voltage), "Core", "Vcore", "Voltage")?.Value, 0, 3);

        var coreLoads = loads
            .Where(s => s.Name.IndexOf("Core #", StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(s => (Name: s.Name, Value: s.Value, Key: ParseCoreKey(s.Name)))
            .OrderBy(t => t.Key.Core).ThenBy(t => t.Key.Thread)
            .Take(32).ToList();
        foreach (var t in coreLoads)
            c.Cores.Add((CoreShortName(t.Name), t.Value));
    }

    // LHM names per-thread load sensors "CPU Core #N" or "CPU Core #N Thread #M".
    // Parse the numbers so C10 sorts after C2 instead of before it.
    private static (int Core, int Thread) ParseCoreKey(string name)
    {
        var m = Regex.Match(name, @"Core\s*#(\d+)(?:\s*Thread\s*#(\d+))?", RegexOptions.IgnoreCase);
        int core = m.Success && int.TryParse(m.Groups[1].Value, out int c) ? c : int.MaxValue;
        int thread = m.Success && m.Groups[2].Success && int.TryParse(m.Groups[2].Value, out int t) ? t : 0;
        return (core, thread);
    }

    private static string CoreShortName(string name)
    {
        var m = Regex.Match(name, @"Core\s*#(\d+)(?:\s*Thread\s*#(\d+))?", RegexOptions.IgnoreCase);
        if (!m.Success) return Shorten(name, 6);
        return m.Groups[2].Success ? $"C{m.Groups[1].Value}T{m.Groups[2].Value}" : $"C{m.Groups[1].Value}";
    }

    private GpuStats CollectGpu(IHardware hw)
    {
        var g = new GpuStats { Name = Shorten(hw.Name, 40) };
        var temps = SensorsOf(hw, SensorType.Temperature);
        var loads = SensorsOf(hw, SensorType.Load);
        var clocks = SensorsOf(hw, SensorType.Clock);

        // Plausibility-window every reading: on hardware LHM only half-supports
        // it emits zeros, negatives or doubled clocks instead of NaN, and those
        // must not reach the UI. Fallback names cover NV / AMD / Intel iGPU
        // sensor naming differences.
        g.Temp = Format.Sane(Find(temps, "GPU Core", "Core", "Temperature")?.Value, -20, 160);
        g.HotSpot = Format.Sane(Find(temps, "Hot Spot")?.Value, -20, 170);
        g.Load = Format.Sane(Find(loads, "GPU Core", "Graphics", "Core")?.Value, 0, 100);
        g.CoreClock = Format.Sane(Find(clocks, "GPU Core", "Graphics", "Core")?.Value, 1, 12000);
        g.MemClock = Format.Sane(Find(clocks, "Memory")?.Value, 1, 30000);
        g.FanRpm = Format.Sane(Find(SensorsOf(hw, SensorType.Fan), "GPU", "Fan")?.Value, 0, 30000);
        g.FanPercent = Format.Sane(Find(SensorsOf(hw, SensorType.Control), "GPU", "Fan")?.Value, 0, 100);
        g.Power = Format.Sane(Find(SensorsOf(hw, SensorType.Power), "GPU", "Package")?.Value, 0, 1500);

        var small = SensorsOf(hw, SensorType.SmallData);
        var usedMb = Find(small, "Memory Used", "Used")?.Value;
        var totalMb = Find(small, "Memory Total", "Total")?.Value;
        if (usedMb.HasValue && usedMb.Value >= 0) g.MemUsedGB = usedMb.Value / 1024f;
        if (totalMb.HasValue && totalMb.Value > 0) g.MemTotalGB = totalMb.Value / 1024f;
        return g;
    }

    private void CollectMemory(IHardware hw, Snapshot snap)
    {
        var m = snap.Memory;
        m.LoadPercent = Format.Sane(Find(SensorsOf(hw, SensorType.Load), "Memory")?.Value, 0, 100);
        var datas = SensorsOf(hw, SensorType.Data);
        var used = Find(datas, "Used Memory", "Used")?.Value;
        var avail = Find(datas, "Available Memory", "Available")?.Value;
        m.UsedGB = used;
        if (used.HasValue && avail.HasValue) m.TotalGB = used.Value + avail.Value;
    }

    private void CollectDrive(IHardware hw, Snapshot snap)
    {
        if (snap.Drives.Count >= 4) return;
        var t = Format.Sane(Find(SensorsOf(hw, SensorType.Temperature), "Temperature")?.Value, -20, 120);
        // LHM exposes SMART remaining life as a Level sensor named "Life".
        var life = Format.Sane(Find(SensorsOf(hw, SensorType.Level), "Life")?.Value, 0, 100);
        snap.Drives.Add(new DriveStats { Name = Shorten(hw.Name, 30), Temp = t, Life = life });
    }

    private void CollectNetwork(IHardware hw, Snapshot snap)
    {
        var thr = SensorsOf(hw, SensorType.Throughput);
        float down = Find(thr, "Download")?.Value ?? 0;
        float up = Find(thr, "Upload")?.Value ?? 0;
        // NaN throughput (unreadable sensor) must not become a candidate:
        // NaN <= 0 is false, so the old check let it through and poisoned the sparklines.
        if (Format.Bad(down)) down = 0;
        if (Format.Bad(up)) up = 0;
        if (down + up <= 0) return;
        var datas = SensorsOf(hw, SensorType.Data);
        snap.NetCandidates.Add(new NetCandidate
        {
            Name = Shorten(hw.Name, 30),
            Down = down,
            Up = up,
            DownTotalGB = Find(datas, "Downloaded")?.Value,
            UpTotalGB = Find(datas, "Uploaded")?.Value,
        });
    }

    private void CollectBoard(IHardware hw, Snapshot snap)
    {
        foreach (var s in SensorsOf(hw, SensorType.Fan))
        {
            if (snap.Fans.Count >= 8) break;
            var rpm = Format.Sane(s.Value, 0, 30000);
            if (rpm.HasValue) snap.Fans.Add(new FanStats { Name = Shorten(s.Name, 24), Rpm = rpm });
        }
        foreach (var s in SensorsOf(hw, SensorType.Temperature))
        {
            if (snap.BoardTemps.Count >= 10) break;
            var v = Format.Sane(s.Value, -20, 160);
            if (v.HasValue)
                snap.BoardTemps.Add(new TempStats { Name = Shorten(s.Name, 24), Value = v.Value });
        }
    }

    private Snapshot DemoSnapshot()
    {
        var r = _rng;
        _demoCpuT = Math.Clamp(_demoCpuT + (float)(r.NextDouble() - 0.5) * 5, 38, 89);
        _demoGpuT = Math.Clamp(_demoGpuT + (float)(r.NextDouble() - 0.5) * 5, 35, 84);
        _demoCpuLoad = Math.Clamp(_demoCpuLoad + (float)(r.NextDouble() - 0.5) * 22, 4, 99);
        _demoGpuLoad = Math.Clamp(_demoGpuLoad + (float)(r.NextDouble() - 0.5) * 24, 2, 100);

        var snap = new Snapshot { Uptime = TimeSpan.FromHours(6.5 + r.NextDouble() * 3) };
        snap.Cpu.Name = "AMD Ryzen 7 7800X3D (demo)";
        snap.Cpu.PackageTemp = _demoCpuT;
        snap.Cpu.Load = _demoCpuLoad;
        snap.Cpu.AvgClock = 4400 + r.Next(-500, 600);
        snap.Cpu.MaxClock = 5050;
        snap.Cpu.Power = 65 + r.Next(0, 55);
        snap.Cpu.Voltage = 1.18f + (float)r.NextDouble() * 0.15f;
        for (int i = 0; i < 32; i++)
            snap.Cpu.Cores.Add(($"C{(i / 2) + 1}T{(i % 2) + 1}", Math.Clamp(_demoCpuLoad + (float)(r.NextDouble() - 0.5) * 40, 1, 100)));

        snap.Gpus.Add(new GpuStats
        {
            Name = "NVIDIA GeForce RTX 4070 (demo)",
            Temp = _demoGpuT,
            HotSpot = _demoGpuT + 11,
            Load = _demoGpuLoad,
            CoreClock = 2310 + r.Next(-120, 180),
            MemClock = 10501,
            FanRpm = 900 + r.Next(0, 900),
            FanPercent = 38 + r.Next(0, 20),
            Power = 120 + r.Next(0, 80),
            MemUsedGB = 6.4f + (float)r.NextDouble() * 2,
            MemTotalGB = 12,
        });

        snap.Memory = new MemStats { LoadPercent = 44 + r.Next(-6, 8), UsedGB = 14.2f, TotalGB = 32 };
        snap.Drives.Add(new DriveStats { Name = "Samsung 990 Pro 2TB", Temp = 41 + r.Next(-2, 3), Life = 96 });
        snap.Drives.Add(new DriveStats { Name = "WD Black SN850X 1TB", Temp = 44 + r.Next(-2, 3), Life = 91 });
        for (int i = 0; i < 4; i++)
            snap.Fans.Add(new FanStats { Name = $"Fan #{i + 1}", Rpm = 800 + r.Next(0, 700) });
        snap.BoardTemps.Add(new TempStats { Name = "Motherboard", Value = 36 + r.Next(-2, 3) });
        snap.BoardTemps.Add(new TempStats { Name = "Chipset", Value = 48 + r.Next(-2, 3) });
        snap.Network = new NetStats
        {
            Name = "Ethernet (demo)",
            DownBytes = (float)(2_000_000 + r.NextDouble() * 40_000_000),
            UpBytes = (float)(200_000 + r.NextDouble() * 3_000_000),
            DownTotalGB = 84.2f,
            UpTotalGB = 9.7f,
        };
        return snap;
    }

    public void Dispose()
    {
        try { _computer.Close(); } catch { }
    }
}

/// <summary>Running tally for one ping target: attempts, replies, RTT stats.
/// Pure bookkeeping (no I/O) so the loss/latency math is headless-testable.</summary>
public sealed class PingTally
{
    public int Sent;
    public int Received;
    private long _sumMs;
    private long _minMs = long.MaxValue;
    private long _maxMs;

    public void Add(bool ok, long ms)
    {
        Sent++;
        if (!ok) return;
        Received++;
        _sumMs += ms;
        if (ms < _minMs) _minMs = ms;
        if (ms > _maxMs) _maxMs = ms;
    }

    public double LossPct => Sent == 0 ? 0 : (Sent - Received) * 100.0 / Sent;
    public double AvgMs => Received == 0 ? 0 : (double)_sumMs / Received;
    public long MinMs => Received == 0 ? 0 : _minMs;
    public long MaxMs => _maxMs;

    public void Reset()
    {
        Sent = 0; Received = 0; _sumMs = 0; _minMs = long.MaxValue; _maxMs = 0;
    }
}

/// <summary>
/// Manual packet-drop test: 100 large-packet (1472-byte payload) ICMP pings
/// each to the default gateway (LAN side), Cloudflare 1.1.1.1 and Google
/// 8.8.8.8, all in parallel, so a loss can be localized. Runs only when
/// asked; nothing pings in the background.
/// </summary>
public sealed class PacketTest
{
    public const int PingCount = 100;
    public const int PayloadBytes = 1472; // + ICMP/IP headers = full 1500-byte frame
    public const string CloudflareHost = "1.1.1.1";
    public const string GoogleHost = "8.8.8.8";

    private static readonly byte[] Payload = MakePayload();

    private static byte[] MakePayload()
    {
        var b = new byte[PayloadBytes];
        Array.Fill(b, (byte)'x');
        return b;
    }

    public PingTally Gateway { get; } = new();
    public PingTally Cloudflare { get; } = new();
    public PingTally Google { get; } = new();
    public string GatewayHost { get; private set; } = "";
    public bool HasGateway { get; private set; } = true;
    public bool Running { get; private set; }
    public bool Done { get; private set; }
    private CancellationTokenSource? _cts;

    public void Start()
    {
        _cts?.Cancel();
        Gateway.Reset();
        Cloudflare.Reset();
        Google.Reset();
        HasGateway = true;
        Done = false;
        Running = true;
        _cts = new CancellationTokenSource();
        _ = RunAsync(_cts.Token);
    }

    public void Stop() => _cts?.Cancel();

    private async Task RunAsync(CancellationToken ct)
    {
        GatewayHost = FindGateway() ?? "";
        HasGateway = GatewayHost.Length > 0;
        var loops = new List<Task>
        {
            PingLoop(CloudflareHost, Cloudflare, ct),
            PingLoop(GoogleHost, Google, ct),
        };
        if (HasGateway) loops.Add(PingLoop(GatewayHost, Gateway, ct));
        try { await Task.WhenAll(loops); } catch { /* cancelled */ }
        Running = false;
        Done = true;
    }

    private static async Task PingLoop(string host, PingTally tally, CancellationToken ct)
    {
        using var ping = new Ping();
        var options = new PingOptions(64, false); // large packets may fragment rather than false-fail
        for (int i = 0; i < PingCount; i++)
        {
            if (ct.IsCancellationRequested) return;
            bool ok = false;
            long ms = 0;
            try
            {
                var rep = await ping.SendPingAsync(host, 1000, Payload, options);
                if (rep.Status == IPStatus.Success) { ok = true; ms = rep.RoundtripTime; }
            }
            catch { /* DNS/ICMP failure: counts as a drop */ }
            tally.Add(ok, ms);
            if (i < PingCount - 1)
            {
                try { await Task.Delay(100, ct); } catch { return; }
            }
        }
    }

    /// <summary>First IPv4 default gateway on an up, non-loopback adapter.</summary>
    public static string? FindGateway()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var gw in ni.GetIPProperties().GatewayAddresses)
                {
                    var a = gw.Address;
                    if (a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any))
                        return a.ToString();
                }
            }
        }
        catch { }
        return null;
    }
}

