using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace XeneonDash;

/// <summary>Fixed-capacity ring buffer of recent sensor samples, oldest → newest.</summary>
public sealed class HistoryBuffer
{
    private readonly float[] _buf;
    private int _head; // next write position
    private int _count;

    public HistoryBuffer(int capacity = 240)
    {
        _buf = new float[Math.Max(1, capacity)];
    }

    public int Count => _count;
    public int Capacity => _buf.Length;

    /// <summary>Forget every sample (session stats reset).</summary>
    public void Clear()
    {
        _head = 0;
        _count = 0;
    }

    public void Push(float v)
    {
        _buf[_head] = v;
        _head = (_head + 1) % _buf.Length;
        if (_count < _buf.Length) _count++;
    }

    /// <summary>Copies samples oldest-first into <paramref name="out"/>, skipping NaN/Infinity.</summary>
    public void CopyValidTo(List<float> @out)
    {
        @out.Clear();
        for (int i = 0; i < _count; i++)
        {
            float v = _buf[(_head - _count + i + _buf.Length) % _buf.Length];
            if (!float.IsNaN(v) && !float.IsInfinity(v)) @out.Add(v);
        }
    }

    public float Latest
    {
        get
        {
            for (int i = 1; i <= _count; i++)
            {
                float v = _buf[(_head - i + _buf.Length) % _buf.Length];
                if (!float.IsNaN(v) && !float.IsInfinity(v)) return v;
            }
            return float.NaN;
        }
    }
}

/// <summary>Per-metric rolling history, sampled at most once per second.</summary>
public sealed class HistoryData
{
    public readonly HistoryBuffer CpuTemp = new();
    public readonly HistoryBuffer CpuLoad = new();
    public readonly HistoryBuffer GpuTemp = new();
    public readonly HistoryBuffer GpuLoad = new();
    public readonly HistoryBuffer MemPct = new();
    public readonly HistoryBuffer NetDown = new(); // bytes/sec
    public readonly HistoryBuffer NetUp = new();   // bytes/sec

    private DateTime _lastPush = DateTime.MinValue;
    public int Samples { get; private set; }

    private static void PushGood(HistoryBuffer b, float? v)
    {
        if (!Format.Bad(v)) b.Push(v!.Value);
    }

    /// <summary>Forget every sample in every buffer (session stats reset).</summary>
    public void Clear()
    {
        CpuTemp.Clear(); CpuLoad.Clear(); GpuTemp.Clear(); GpuLoad.Clear();
        MemPct.Clear(); NetDown.Clear(); NetUp.Clear();
        Samples = 0;
    }

    public void Push(Snapshot s)
    {
        var now = DateTime.Now;
        if ((now - _lastPush).TotalSeconds < 1.0) return; // 1 sample/sec max
        _lastPush = now;
        Samples++;

        PushGood(CpuTemp, s.Cpu.PackageTemp);
        PushGood(CpuLoad, s.Cpu.Load);
        var gpu = s.Gpus.FirstOrDefault();
        if (gpu != null)
        {
            PushGood(GpuTemp, gpu.Temp);
            PushGood(GpuLoad, gpu.Load);
        }
        PushGood(MemPct, s.Memory.LoadPercent);
        if (!Format.Bad(s.Network.DownBytes)) NetDown.Push(s.Network.DownBytes!.Value);
        if (!Format.Bad(s.Network.UpBytes)) NetUp.Push(s.Network.UpBytes!.Value);
    }
}

/// <summary>Session-wide min/max/average per metric key, since the app started.</summary>
public sealed class SessionStats
{
    private sealed class Acc
    {
        public float Min = float.MaxValue;
        public float Max = float.MinValue;
        public double Sum;
        public int N;
    }

    private readonly Dictionary<string, Acc> _d = new();

    public void Add(string key, float? v)
    {
        if (Format.Bad(v)) return;
        float f = v!.Value;
        if (!_d.TryGetValue(key, out var a)) _d[key] = a = new Acc();
        if (f < a.Min) a.Min = f;
        if (f > a.Max) a.Max = f;
        a.Sum += f;
        a.N++;
    }

    /// <summary>Forget every recorded min/max/avg (session stats reset).</summary>
    public void Reset() => _d.Clear();

    public bool TryGet(string key, out float min, out float max, out float avg)
    {
        if (_d.TryGetValue(key, out var a) && a.N > 0)
        {
            min = a.Min; max = a.Max; avg = (float)(a.Sum / a.N);
            return true;
        }
        min = max = avg = 0;
        return false;
    }

    public string Summary(string key, Func<float, string> fmt) =>
        TryGet(key, out var mn, out var mx, out var av)
            ? $"MIN {fmt(mn)}  ·  MAX {fmt(mx)}  ·  AVG {fmt(av)}"
            : "";

    /// <summary>Uniform-unit summary: the formatter receives (min, max, avg) and
    /// picks ONE unit for all three, so the line never mixes MHz/GHz or KB/s/MB/s.</summary>
    public string Summary(string key, Func<float, float, float, string> fmt) =>
        TryGet(key, out var mn, out var mx, out var av) ? fmt(mn, mx, av) : "";

    /// <summary>Feed the headline metrics from a fresh snapshot.</summary>
    public void Record(Snapshot s)
    {
        Add("cpu.temp", s.Cpu.PackageTemp);
        Add("cpu.load", s.Cpu.Load);
        Add("cpu.clock", s.Cpu.AvgClock);
        Add("cpu.power", s.Cpu.Power);
        var gpu = s.Gpus.FirstOrDefault();
        if (gpu != null)
        {
            Add("gpu.temp", gpu.Temp);
            Add("gpu.hotspot", gpu.HotSpot);
            Add("gpu.load", gpu.Load);
            Add("gpu.power", gpu.Power);
            Add("gpu.clock", gpu.CoreClock);
        }
        Add("mem.pct", s.Memory.LoadPercent);
        Add("net.down", s.Network.DownBytes);
        Add("net.up", s.Network.UpBytes);
        for (int i = 0; i < s.Drives.Count; i++)
        {
            Add($"drive{i}.temp", s.Drives[i].Temp);
            Add($"drive{i}.life", s.Drives[i].Life);
        }
        for (int i = 0; i < s.BoardTemps.Count; i++)
            Add($"board{i}.temp", s.BoardTemps[i].Value);
    }
}

/// <summary>Appends sensor snapshots to a per-day CSV file. Failures are swallowed:
/// logging must never take down the dashboard.</summary>
public sealed class CsvLogger : IDisposable
{
    public bool Enabled { get; set; }

    public static string LogDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XeneonDash", "logs");

    private DateTime _lastWrite = DateTime.MinValue;
    private string _openDay = "";
    private StreamWriter? _w;

    private static string Cell(float? v) =>
        Format.Bad(v) ? "" : v!.Value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string NameCell(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

    private static string Header()
    {
        var cols = new List<string>
        {
            "timestamp", "cpu_temp_c", "cpu_load_pct", "cpu_clock_mhz", "cpu_power_w",
            "gpu_name", "gpu_temp_c", "gpu_load_pct", "gpu_mem_used_gb", "gpu_mem_total_gb",
            "mem_used_gb", "mem_total_gb", "net_down_bps", "net_up_bps",
        };
        for (int i = 0; i < 4; i++)
        {
            cols.Add($"drive{i}_name");
            cols.Add($"drive{i}_temp_c");
            cols.Add($"drive{i}_life_pct");
        }
        return string.Join(",", cols);
    }

    private static string Row(Snapshot s, DateTime now)
    {
        var cols = new List<string> { now.ToString("yyyy-MM-dd HH:mm:ss") };
        cols.Add(Cell(s.Cpu.PackageTemp));
        cols.Add(Cell(s.Cpu.Load));
        cols.Add(Cell(s.Cpu.AvgClock));
        cols.Add(Cell(s.Cpu.Power));
        var gpu = s.Gpus.FirstOrDefault();
        cols.Add(NameCell(gpu?.Name ?? ""));
        cols.Add(Cell(gpu?.Temp));
        cols.Add(Cell(gpu?.Load));
        cols.Add(Cell(gpu?.MemUsedGB));
        cols.Add(Cell(gpu?.MemTotalGB));
        cols.Add(Cell(s.Memory.UsedGB));
        cols.Add(Cell(s.Memory.TotalGB));
        cols.Add(Cell(s.Network.DownBytes));
        cols.Add(Cell(s.Network.UpBytes));
        for (int i = 0; i < 4; i++)
        {
            if (i < s.Drives.Count)
            {
                cols.Add(NameCell(s.Drives[i].Name));
                cols.Add(Cell(s.Drives[i].Temp));
                cols.Add(Cell(s.Drives[i].Life));
            }
            else { cols.Add(""); cols.Add(""); cols.Add(""); }
        }
        return string.Join(",", cols);
    }

    /// <summary>Append one row, at most every 5 seconds.</summary>
    public void MaybeLog(Snapshot s)
    {
        if (!Enabled) return;
        var now = DateTime.Now;
        if ((now - _lastWrite).TotalSeconds < 5) return;
        _lastWrite = now;
        try
        {
            string day = now.ToString("yyyy-MM-dd");
            if (_w == null || _openDay != day)
            {
                try { _w?.Dispose(); } catch { }
                _w = null;
                Directory.CreateDirectory(LogDir);
                string path = Path.Combine(LogDir, $"xeneondash-{day}.csv");
                bool isNew = !File.Exists(path);
                _w = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    AutoFlush = true,
                };
                if (isNew) _w.WriteLine(Header());
                _openDay = day;
            }
            _w!.WriteLine(Row(s, now));
        }
        catch
        {
            // Never crash the dashboard because logging failed.
            try { _w?.Dispose(); } catch { }
            _w = null;
            _openDay = "";
        }
    }

    public void Dispose()
    {
        try { _w?.Dispose(); } catch { }
        _w = null;
    }
}

/// <summary>Start-with-Windows via HKCU\...\Run. No admin needed.</summary>
/// <summary>
/// Start-with-Windows via a logon scheduled task that runs with the highest
/// privileges: the app gets admin rights (needed for GPU/SMU sensors) at
/// sign-in WITHOUT a UAC prompt — a registry Run entry can't elevate.
/// Creating/deleting the task itself needs admin, so a non-elevated user is
/// offered a one-time elevated helper run (see ApplyWithElevation).
/// </summary>
public static class StartupManager
{
    private const string TaskName = "XeneonDash";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "XeneonDash";

    public static bool IsElevated
    {
        get
        {
            try
            {
                using var id = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }

    /// <summary>True when the elevated logon task exists (query needs no admin).</summary>
    public static bool IsEnabled()
    {
        try { return SchTasks(out _, "/Query", "/TN", TaskName) == 0; }
        catch { return false; }
    }

    /// <summary>
    /// Create or delete the logon task. Requires elevation; returns false when
    /// not elevated or when schtasks failed. Also clears the pre-task registry
    /// Run entry, which launched the app unelevated.
    /// </summary>
    public static bool SetEnabled(bool on)
    {
        try
        {
            RemoveLegacyRunEntry();
            if (!IsElevated) return false;
            if (on) return TryCreateTaskXml() || CreateTaskFlags();
            return SchTasks(out _, "/Delete", "/TN", TaskName, "/F") == 0;
        }
        catch { return false; }
    }

    /// <summary>Plain-flags creation. Works everywhere, but Task Scheduler's
    /// default 72-hour execution limit can stop a long-lived dashboard.</summary>
    private static bool CreateTaskFlags()
    {
        string exe = Application.ExecutablePath;
        return SchTasks(out _, "/Create", "/TN", TaskName,
            "/TR", "\"" + exe + "\"",
            "/SC", "ONLOGON", "/RL", "HIGHEST", "/F") == 0;
    }

    /// <summary>XML creation: same task but with NO execution-time limit and a
    /// single-instance policy — a dashboard is meant to run until sign-out.</summary>
    private static bool TryCreateTaskXml()
    {
        string? file = null;
        try
        {
            string xml =
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                "  <RegistrationInfo><Description>XeneonDash dashboard (starts with Windows, elevated for sensor access)</Description></RegistrationInfo>\r\n" +
                "  <Triggers><LogonTrigger><Enabled>true</Enabled></LogonTrigger></Triggers>\r\n" +
                "  <Principals><Principal id=\"Author\">" +
                "<UserId>" + Xml(WindowsIdentity.GetCurrent().Name) + "</UserId>" +
                "<LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n" +
                "  <Settings>" +
                "<MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>" +
                "<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
                "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>" +
                "<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>" +
                "<Enabled>true</Enabled></Settings>\r\n" +
                "  <Actions Context=\"Author\"><Exec><Command>" + Xml(Application.ExecutablePath) + "</Command></Exec></Actions>\r\n" +
                "</Task>";
            file = Path.Combine(Path.GetTempPath(), "XeneonDash-task.xml");
            File.WriteAllText(file, xml, Encoding.Unicode);
            return SchTasks(out _, "/Create", "/TN", TaskName, "/XML", file, "/F") == 0;
        }
        catch { return false; }
        finally { try { if (file != null) File.Delete(file); } catch { } }
    }

    private static string Xml(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>
    /// Apply the task state from a non-elevated session: relaunches this exe
    /// once with the runas verb (one UAC prompt) in --apply-startup mode.
    /// Returns whether the desired state is in effect afterwards.
    /// </summary>
    public static bool ApplyWithElevation(bool on)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Application.ExecutablePath,
                Arguments = "--apply-startup " + (on ? "on" : "off"),
                Verb = "runas",
                UseShellExecute = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(30000);
            return IsEnabled() == on;
        }
        catch { return false; } // UAC declined or launch failed
    }

    /// <summary>The old registry autostart launched unelevated; remove it (HKCU needs no admin).</summary>
    public static void RemoveLegacyRunEntry()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            k?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch { }
    }

    private static int SchTasks(out string output, params string[] args)
    {
        output = "";
        var psi = new ProcessStartInfo("schtasks.exe")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi);
        if (p == null) return -1;
        output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        if (!p.WaitForExit(15000))
        {
            try { p.Kill(); } catch { }
            return -1;
        }
        return p.ExitCode;
    }
}
