using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Pakban.Localization;
using Pakban.Models;
using Pakban.Services;
using Pakban.Views;
using Serilog;

namespace Pakban;

public partial class App : Application
{
    private static string? _lastErrorKey;
    private static DateTime _lastErrorTime;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 1. Initialize logging FIRST before any services or UI components run
        LoggingService.Initialize();

        // 2. Global safety net: log unhandled crashes with Serilog
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                Log.Fatal(ex, "Unhandled AppDomain exception. IsTerminating: {IsTerminating}", args.IsTerminating);
                LogException(ex);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved Task exception");
            args.SetObserved();
        };

        // 3. Load user settings
        var settings = SettingsService.Load();
        Loc.I.SetLanguage(settings.Language, save: false);
        ThemeManager.Initialize();
        ThemeManager.ApplyTheme(settings.Theme, save: false);
        AccentTheme.Apply(settings.Accent);
        SettingsService.Current = settings;

        if (e.Args.Any(a => a.Equals("--selftest-scan", StringComparison.OrdinalIgnoreCase)))
        {
            StartSelfTestScan();
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Flush all pending Serilog entries on application shutdown
        LoggingService.CloseAndFlush();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled Dispatcher exception caught");
        LogException(e.Exception);

        // Same failure firing again within 5s → log it, but don't flood the screen.
        var key = e.Exception.GetType().Name + "|" + e.Exception.Message;
        if (key == _lastErrorKey && (DateTime.Now - _lastErrorTime).TotalSeconds < 5)
        {
            e.Handled = true;
            return;
        }
        _lastErrorKey = key;
        _lastErrorTime = DateTime.Now;

        MessageBox.Show(
            $"{e.Exception.Message}\n\n{e.Exception.StackTrace}",
            "Pakban — Unexpected error",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void LogException(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Pakban");

            // SECURITY FIX: Prevent Symlink Arbitrary File Write / Append Attacks (CWE-59)
            if (FileOps.IsReparsePoint(dir)) return;

            Directory.CreateDirectory(dir);
            var logFile = Path.Combine(dir, "error.log");

            if (File.Exists(logFile) && FileOps.IsReparsePoint(logFile))
            {
                File.Delete(logFile); // Delete symlink, do not follow
            }

            File.AppendAllText(logFile,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch { /* best effort */ }
    }

    // ------------------------------------------------------------------ //
    //  Headless scan pipeline test:  Pakban.exe --selftest-scan          //
    // ------------------------------------------------------------------ //

    private static async void StartSelfTestScan()
    {
        var logPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "selftest.log");
        var sb = new StringBuilder();
        try
        {
            sb.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] selftest scan start (elevated: {FileOps.IsElevated})");

            foreach (var category in FileOps.BuildCategories())
            {
                var sw = Stopwatch.StartNew();
                if (category.Kind == CleanKind.RecycleBin)
                {
                    category.Bytes = FileOps.GetRecycleBinSize();
                    category.Scanned = true;
                }
                else
                {
                    foreach (var path in FileOps.ExistingPaths(category))
                    {
                        var (b, f) = category.Kind == CleanKind.Files
                            ? FileOps.MeasureFiles(path, category.FilePattern ?? "*", default)
                            : FileOps.MeasureFolder(path, default);
                        category.Bytes += b;
                        category.FileCount += f;
                    }
                    category.Scanned = true;
                }
                sb.AppendLine($"  {category.Id,-14} {FileOps.FormatSize(category.Bytes),12}  {category.FileCount,8} files  {sw.ElapsedMilliseconds} ms");
            }

            var orphans = await OrphanDetection.FindAsync(default).ConfigureAwait(true);
            sb.AppendLine($"  orphans:       {orphans.Count} items, {FileOps.FormatSize(orphans.Sum(o => o.Bytes))} total, {FileOps.FormatSize(orphans.Count > 0 ? orphans[0].Bytes : 0)} largest");
            foreach (var orphan in orphans.Take(15))
                sb.AppendLine($"    - {FileOps.FormatSize(orphan.Bytes),12}  {orphan.Path}");

            var ram = RamService.GetStats();
            sb.AppendLine($"  RAM: {ram.UsedGB:F1}/{ram.TotalGB:F1} GB ({ram.LoadPercent}%), cached {ram.CachedGB:F1} GB");

            sb.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] selftest scan OK");
        }
        catch (Exception ex)
        {
            sb.AppendLine("SELFTEST FAILED: " + ex);
            LogException(ex);
        }
        try { File.WriteAllText(logPath, sb.ToString()); } catch { }
        Application.Current.Shutdown();
    }
}
