using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace XeneonDash;

internal static class Program
{
    private static Mutex? _instanceMutex;
    private static bool _errorBoxShown;

    [STAThread]
    private static void Main(string[] args)
    {
        // Crash diagnostics: if startup dies, leave the full exception in a
        // log file so the cause can be read instead of guessed.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteCrashLog(e.ExceptionObject);

        // One-time elevated helper: applies the Start-with-Windows task state
        // and exits. Must run BEFORE the single-instance check (the parent app
        // holds the mutex while it waits for this helper).
        int si = Array.IndexOf(args, "--apply-startup");
        if (si >= 0 && si + 1 < args.Length)
        {
            StartupManager.SetEnabled(args[si + 1].Equals("on", StringComparison.OrdinalIgnoreCase));
            return;
        }

        // Single instance: a second launch just exits. Wait briefly on first
        // failure — Application.Restart (theme/text-size change) starts the
        // new instance before the old one has released the mutex.
        _instanceMutex = new Mutex(false, @"Local\XeneonDash");
        bool owned;
        try { owned = _instanceMutex.WaitOne(0); }
        catch (AbandonedMutexException) { owned = true; }
        if (!owned)
        {
            try { owned = _instanceMutex.WaitOne(TimeSpan.FromSeconds(3)); }
            catch (AbandonedMutexException) { owned = true; }
        }
        if (!owned) return;

        try
        {
            var settings = AppSettings.Load();
            Theme.SetCurrent(settings.ThemeId);
            Theme.FontScale = Math.Clamp(settings.FontScalePct, 80, 130) / 100f;
            Theme.InitFonts();
            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) =>
            {
                WriteCrashLog(e.Exception);
                // Log every error, but show the box once: if a paint handler
                // throws on every tick, the app must not bury the user in modals.
                if (_errorBoxShown) return;
                _errorBoxShown = true;
                MessageBox.Show(
                    "XeneonDash hit an error and logged it to XeneonDash-crash.log.\n\n" +
                    e.Exception.GetType().Name + ": " + e.Exception.Message,
                    "XeneonDash error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            bool demo = args.Any(a => a.Equals("--demo", StringComparison.OrdinalIgnoreCase));
            bool layoutDump = args.Any(a => a.Equals("--layout-dump", StringComparison.OrdinalIgnoreCase));
            Application.Run(new MainForm(settings, demo, layoutDump));
        }
        catch (Exception ex)
        {
            WriteCrashLog(ex);
            MessageBox.Show(
                "XeneonDash failed to start and logged the error to XeneonDash-crash.log.\n\n" +
                ex.GetType().Name + ": " + ex.Message,
                "XeneonDash failed to start", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            try { _instanceMutex.ReleaseMutex(); } catch { }
            _instanceMutex.Dispose();
        }
    }

    private static void WriteCrashLog(object ex)
    {
        string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n";
        // Next to the exe first; if that folder isn't writable (e.g. installed
        // under Program Files), fall back to LocalAppData.
        try
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "XeneonDash-crash.log"), entry);
            return;
        }
        catch { }
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XeneonDash");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "XeneonDash-crash.log"), entry);
        }
        catch { }
    }
}
