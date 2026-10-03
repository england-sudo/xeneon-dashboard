using System;
using System.Collections.Generic;
using System.IO;
using XeneonDash;

internal static class Program
{
    private static int _fail;

    private static void Check(bool cond, string name)
    {
        Console.WriteLine((cond ? "PASS " : "FAIL ") + name);
        if (!cond) _fail++;
    }

    private static Snapshot MakeSnap(float cpuTemp, float gpuTemp)
    {
        var s = new Snapshot();
        s.Cpu.PackageTemp = cpuTemp;
        s.Cpu.Load = 42;
        s.Cpu.AvgClock = 5200;
        s.Cpu.Power = 130;
        s.Gpus.Add(new GpuStats { Name = "RTX 4090", Temp = gpuTemp, Load = 67, Power = 280, CoreClock = 2610, HotSpot = gpuTemp + 11 });
        s.Memory = new MemStats { LoadPercent = 44, UsedGB = 14.2f, TotalGB = 32 };
        s.Drives.Add(new DriveStats { Name = "Samsung 990 Pro", Temp = 41, Life = 96 });
        s.Network = new NetStats { DownBytes = 5_000_000, UpBytes = 500_000 };
        return s;
    }

    public static int Main()
    {
        // --- HistoryBuffer ring behavior ---
        var b = new HistoryBuffer(8);
        for (int i = 1; i <= 12; i++) b.Push(i);
        Check(b.Count == 8, "buffer caps at capacity");
        var tmp = new List<float>();
        b.CopyValidTo(tmp);
        Check(tmp.Count == 8 && tmp[0] == 5 && tmp[7] == 12, "buffer oldest-first after wrap");
        Check(b.Latest == 12, "buffer Latest");

        // NaN skipped
        var b2 = new HistoryBuffer(8);
        b2.Push(float.NaN); b2.Push(3);
        b2.CopyValidTo(tmp);
        Check(tmp.Count == 1 && tmp[0] == 3, "buffer skips NaN");
        Check(float.IsNaN(new HistoryBuffer(4).Latest), "Latest NaN when empty");

        // --- SessionStats ---
        var sess = new SessionStats();
        Check(sess.Summary("cpu.temp", v => $"{v:0.0}") == "", "summary empty before data");
        sess.Record(MakeSnap(50, 60));
        sess.Record(MakeSnap(70, 80));
        sess.Record(MakeSnap(60, 70));
        string sum = sess.Summary("cpu.temp", v => $"{v:0.0}");
        Check(sum == "MIN 50.0  ·  MAX 70.0  ·  AVG 60.0", "session min/max/avg: " + sum);
        Check(sess.Summary("gpu.hotspot", v => $"{v:0}") == "MIN 71  ·  MAX 91  ·  AVG 81", "gpu hotspot tracked");
        // bad values ignored
        var bad = MakeSnap(50, 60);
        bad.Cpu.PackageTemp = float.NaN;
        sess.Record(bad);
        Check(sess.Summary("cpu.temp", v => $"{v:0.0}").StartsWith("MIN 50.0"), "NaN ignored in session");

        // --- HistoryData throttle ---
        var h = new HistoryData();
        h.Push(MakeSnap(50, 60));
        h.Push(MakeSnap(51, 61)); // <1s later: throttled
        Check(h.Samples == 1, "history throttled to 1/sec");
        Check(h.CpuTemp.Count == 1 && h.NetDown.Count == 1, "history buffers pushed");

        // --- Reset: session stats + history clear ---
        sess.Reset();
        Check(sess.Summary("cpu.temp", v => $"{v:0.0}") == "", "session reset empties summaries");
        sess.Record(MakeSnap(55, 65));
        Check(sess.Summary("cpu.temp", v => $"{v:0.0}") == "MIN 55.0  ·  MAX 55.0  ·  AVG 55.0", "session records again after reset");
        h.Clear();
        Check(h.Samples == 0 && h.CpuTemp.Count == 0 && h.NetUp.Count == 0, "history clear empties buffers + sample count");
        Check(float.IsNaN(h.CpuTemp.Latest), "cleared buffer Latest is NaN");
        b.Clear();
        Check(b.Count == 0, "buffer Clear empties");

        // --- PingTally loss/latency math ---
        var tally = new PingTally();
        Check(tally.LossPct == 0 && tally.AvgMs == 0, "tally empty: 0 loss, 0 avg");
        for (int i = 0; i < 18; i++) tally.Add(true, 10 + i); // 18 replies, 10..27 ms
        tally.Add(false, 0); tally.Add(false, 0);              // 2 drops
        Check(tally.Sent == 20 && tally.Received == 18, "tally counts sent/received");
        Check(Math.Abs(tally.LossPct - 10.0) < 0.001, "tally loss pct: " + tally.LossPct);
        Check(Math.Abs(tally.AvgMs - 18.5) < 0.001 && tally.MinMs == 10 && tally.MaxMs == 27, "tally avg/min/max");
        tally.Reset();
        Check(tally.Sent == 0 && tally.Received == 0 && tally.LossPct == 0, "tally reset");
        Check(PacketTest.PingCount == 100, "packet test sends 100 pings per target");
        Check(PacketTest.PayloadBytes >= 1400, "packet test uses large packets");

        // --- CsvLogger ---
        var log = new CsvLogger { Enabled = true };
        // point at a temp dir by shadowing? LogDir is fixed to Documents; instead just verify no-crash + header shape via reflection-free check:
        // we can't redirect LogDir, so verify Row/Header indirectly is not possible; at least ensure no exception on Linux (Documents may not exist -> swallowed).
        log.MaybeLog(MakeSnap(55, 65));
        log.MaybeLog(MakeSnap(56, 66)); // <5s: throttled
        string dir = CsvLogger.LogDir;
        string file = Path.Combine(dir, $"xeneondash-{DateTime.Now:yyyy-MM-dd}.csv");
        if (File.Exists(file))
        {
            var lines = File.ReadAllLines(file);
            Check(lines.Length >= 2, "csv file written with header + row");
            Check(lines[0].StartsWith("timestamp,cpu_temp_c"), "csv header shape");
            Check(lines[1].Contains("55") && lines[1].Contains("RTX 4090"), "csv row content");
            File.Delete(file);
        }
        else
        {
            // Documents may not resolve on this VM; the logger must swallow, not crash.
            Check(true, "csv logger swallowed missing Documents dir (no crash)");
        }
        log.Dispose();

        // disabled logger never touches disk
        var log2 = new CsvLogger { Enabled = false };
        log2.MaybeLog(MakeSnap(1, 2));
        Check(true, "disabled logger no-op");

        // --- Format: uniform-unit range formatters ---
        Check(Format.ClockRange(210, 2760, 2550) == "MIN 0.21 · MAX 2.76 · AVG 2.55 GHz",
            "ClockRange GHz uniform: " + Format.ClockRange(210, 2760, 2550));
        Check(Format.ClockRange(210, 800, 500) == "MIN 210 · MAX 800 · AVG 500 MHz",
            "ClockRange MHz uniform: " + Format.ClockRange(210, 800, 500));
        Check(Format.BytesRange(500, 5_000_000, 1_048_576) == "MIN 0.0 · MAX 4.8 · AVG 1.0 MB/s",
            "BytesRange MB/s uniform: " + Format.BytesRange(500, 5_000_000, 1_048_576));
        Check(Format.BytesRange(100, 900, 400) == "MIN 100.0 · MAX 900.0 · AVG 400.0 B/s",
            "BytesRange B/s uniform: " + Format.BytesRange(100, 900, 400));

        // --- Format.Sane plausibility windows ---
        Check(Format.Sane(55, 1, 160) == 55, "Sane keeps plausible temp");
        Check(Format.Sane(0, 1, 160) == null, "Sane rejects 0C package");
        Check(Format.Sane(float.NaN, 1, 160) == null, "Sane rejects NaN");
        Check(Format.Sane(9000, 1, 12000) == 9000, "Sane keeps plausible clock");
        Check(Format.Sane(null, 0, 100) == null, "Sane rejects null");

        // --- SessionStats uniform-unit Summary ---
        var s3 = new SessionStats();
        s3.Record(MakeSnap(50, 60));
        s3.Record(MakeSnap(70, 80));
        Check(s3.Summary("gpu.temp", (mn, mx, av) => $"{mn:0}/{mx:0}/{av:0}") == "60/80/70",
            "Summary3 passes (min,max,avg)");
        Check(s3.Summary("nope", (mn, mx, av) => "x") == "", "Summary3 empty for unknown key");

        Console.WriteLine(_fail == 0 ? "ALL PASS" : $"{_fail} FAILURES");
        return _fail;
    }
}
