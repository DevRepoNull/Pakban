using System.IO;
using System.Text;
using Serilog;

namespace Pakban.Services;

/// <summary>
/// Provides configuration and lifecycle management for Serilog file logging.
/// Writes daily rotating logs to AppData/Roaming/Pakban/logs/app-yyyyMMdd.log.
/// </summary>
public static class LoggingService
{
    public static string LogDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pakban", "logs");

    /// <summary>
    /// Configures and initializes the Serilog static logger instance with daily rotating files.
    /// File pattern: app-yyyyMMdd.log (e.g., app-20261025.log)
    /// </summary>
    public static void Initialize()
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);

            var logPathPattern = Path.Combine(LogDirectory, "app-.log");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    path: logPathPattern,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                    encoding: Encoding.UTF8)
                .CreateLogger();

            Log.Information("Pakban logging initialized. Target log directory: {LogDirectory}", LogDirectory);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Failed to initialize Serilog: {ex.Message}");
        }
    }

    /// <summary>
    /// Flushes and closes all Serilog sinks before application termination.
    /// </summary>
    public static void CloseAndFlush()
    {
        Log.Information("Pakban application shutting down. Closing logger.");
        Log.CloseAndFlush();
    }
}
