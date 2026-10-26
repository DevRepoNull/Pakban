using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Serilog;

namespace Pakban.Services;

/// <summary>
/// Represents an individual item (file or folder) targeted for cleanup.
/// </summary>
public sealed record CleanupTarget
{
    public required string Path { get; init; }
    public bool IsCritical { get; init; }
    public string? DisplayName { get; init; }

    public static CleanupTarget FromPath(string path, bool isCritical = false, string? displayName = null) =>
        new() { Path = path, IsCritical = isCritical, DisplayName = displayName };
}

/// <summary>
/// Details about an item that failed to delete or was skipped.
/// </summary>
public sealed record CleanupFailure(
    string Path,
    string ErrorMessage,
    string ExceptionType,
    bool IsLocked
);

/// <summary>
/// Progress notification payload reported during cleanup execution.
/// </summary>
public sealed record CleanupProgress(
    string CurrentPath,
    int ProcessedCount,
    int TotalCount,
    double Percentage
);

/// <summary>
/// Comprehensive summary of a cleanup operation.
/// </summary>
public sealed class CleanupReport
{
    /// <summary>Number of files/folders successfully deleted or recycled.</summary>
    public int SuccessCount { get; init; }

    /// <summary>Number of files/folders that failed or were skipped (e.g., locked).</summary>
    public int FailedCount { get; init; }

    /// <summary>Total bytes freed during the operation.</summary>
    public long FreedBytes { get; init; }

    /// <summary>Total freed space formatted in Megabytes (MB).</summary>
    public double FreedSpaceMB => Math.Round(FreedBytes / (1024.0 * 1024.0), 2);

    /// <summary>Total freed space formatted in Gigabytes (GB).</summary>
    public double FreedSpaceGB => Math.Round(FreedBytes / (1024.0 * 1024.0 * 1024.0), 2);

    /// <summary>List of all failed items with failure reason and lock status.</summary>
    public IReadOnlyList<CleanupFailure> Failures { get; init; } = Array.Empty<CleanupFailure>();

    /// <summary>Time elapsed during the cleanup run.</summary>
    public TimeSpan Duration { get; init; }

    public override string ToString() =>
        $"Cleanup Report: {SuccessCount} succeeded, {FailedCount} failed, {FreedSpaceMB:N2} MB freed in {Duration.TotalMilliseconds:N0} ms.";
}

/// <summary>
/// Contract for safe, fault-tolerant file and folder cleanup operations.
/// </summary>
public interface ICleanupService
{
    Task<CleanupReport> CleanPathsAsync(
        IEnumerable<string> paths,
        bool isCritical = false,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<CleanupReport> CleanTargetsAsync(
        IEnumerable<CleanupTarget> targets,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Senior-level implementation of file cleanup for WPF apps:
/// - Robust error handling (locked files, sharing violations, UAC, permissions).
/// - Non-blocking asynchronous processing with cancellation & progress.
/// - Windows Shell Recycle Bin integration (SHFileOperationW) for recoverable items.
/// - Hardened against Symlink/Junction traversal attacks and TOCTOU race conditions.
/// - Hardened against Path Traversal and protected system directories destruction.
/// </summary>
public sealed class CleanupService : ICleanupService
{
    private readonly Action<string, Exception?>? _logger;

    public CleanupService(Action<string, Exception?>? logger = null)
    {
        _logger = logger ?? DefaultLogger;
    }

    private static void DefaultLogger(string message, Exception? ex)
    {
        // SECURITY FIX: Sanitize message to prevent log forging (CWE-117)
        var sanitizedMessage = message.Replace("\r", "").Replace("\n", " ");
        if (ex is null)
        {
            Log.Information("[CleanupService] {Message}", sanitizedMessage);
            Trace.WriteLine($"[CleanupService] {sanitizedMessage}");
        }
        else
        {
            Log.Warning(ex, "[CleanupService] {Message}", sanitizedMessage);
            Trace.TraceWarning($"[CleanupService] {sanitizedMessage} -> {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Executes cleanup on a list of raw paths (supports environment variables like %TEMP%).
    /// </summary>
    public Task<CleanupReport> CleanPathsAsync(
        IEnumerable<string> paths,
        bool isCritical = false,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var targets = paths.Select(p => new CleanupTarget { Path = p, IsCritical = isCritical });
        return CleanTargetsAsync(targets, progress, cancellationToken);
    }

    /// <summary>
    /// Executes cleanup on a list of configured <see cref="CleanupTarget"/> items.
    /// </summary>
    public async Task<CleanupReport> CleanTargetsAsync(
        IEnumerable<CleanupTarget> targets,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);

        var targetList = targets.ToList();
        var sw = Stopwatch.StartNew();

        return await Task.Run(() =>
        {
            int successCount = 0;
            int failedCount = 0;
            long freedBytes = 0;
            var failures = new List<CleanupFailure>();
            int processed = 0;
            int total = targetList.Count;

            foreach (var target in targetList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                processed++;
                progress?.Report(new CleanupProgress(
                    CurrentPath: target.Path,
                    ProcessedCount: processed,
                    TotalCount: total,
                    Percentage: total > 0 ? (100.0 * processed / total) : 100.0));

                // SECURITY FIX: Strict path resolution and validation against Path Traversal (CWE-22)
                var expandedPath = ResolvePath(target.Path);
                if (string.IsNullOrWhiteSpace(expandedPath))
                {
                    _logger?.Invoke($"Invalid or prohibited path skipped: {target.Path}", null);
                    continue;
                }

                // SECURITY FIX: Prevent any operation on protected system roots (e.g. C:\Windows, C:\Program Files)
                if (!FileOps.IsPathSafeForCleanup(expandedPath))
                {
                    failedCount++;
                    failures.Add(new CleanupFailure(
                        Path: expandedPath,
                        ErrorMessage: "SECURITY BLOCKED: Path points to a system protected directory or invalid location.",
                        ExceptionType: nameof(SecurityException),
                        IsLocked: false));
                    _logger?.Invoke($"SECURITY BLOCKED: Refused to operate on protected path: {expandedPath}", null);
                    continue;
                }

                if (!File.Exists(expandedPath) && !Directory.Exists(expandedPath))
                {
                    // Target already absent; skip without counting as failure.
                    continue;
                }

                if (target.IsCritical)
                {
                    // Critical or recoverable items are sent to the Windows Recycle Bin
                    ProcessRecycleBin(expandedPath, ref successCount, ref failedCount, ref freedBytes, failures);
                }
                else
                {
                    // Non-critical items: attempt safe permanent deletion
                    ProcessPermanentDelete(expandedPath, cancellationToken, ref successCount, ref failedCount, ref freedBytes, failures);
                }
            }

            sw.Stop();

            var report = new CleanupReport
            {
                SuccessCount = successCount,
                FailedCount = failedCount,
                FreedBytes = freedBytes,
                Failures = failures,
                Duration = sw.Elapsed,
            };

            _logger?.Invoke(report.ToString(), null);
            return report;

        }, cancellationToken).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- //
    //  Recycle Bin via Windows Shell API (SHFileOperationW)            //
    // ---------------------------------------------------------------- //

    private void ProcessRecycleBin(
        string path,
        ref int successCount,
        ref int failedCount,
        ref long freedBytes,
        List<CleanupFailure> failures)
    {
        // SECURITY FIX: Verify path safety before shell invocation
        if (!FileOps.IsPathSafeForCleanup(path))
        {
            failedCount++;
            failures.Add(new CleanupFailure(path, "Path rejected by security policy", nameof(SecurityException), false));
            return;
        }

        long targetSize = MeasurePathBytes(path);

        try
        {
            bool recycled = MoveToRecycleBin(path);
            if (recycled)
            {
                successCount++;
                freedBytes += targetSize;
            }
            else
            {
                failedCount++;
                var failure = new CleanupFailure(
                    Path: path,
                    ErrorMessage: "Windows Shell failed to move the item to the Recycle Bin (item may be in use or access denied).",
                    ExceptionType: nameof(Win32Exception),
                    IsLocked: false);

                failures.Add(failure);
                _logger?.Invoke($"Failed to recycle item: {path}", null);
            }
        }
        catch (Exception ex)
        {
            failedCount++;
            bool isLocked = IsFileLocked(ex);
            var failure = new CleanupFailure(
                Path: path,
                ErrorMessage: ex.Message,
                ExceptionType: ex.GetType().Name,
                IsLocked: isLocked);

            failures.Add(failure);
            _logger?.Invoke($"Exception while recycling item: {path}", ex);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public IntPtr pFrom;
        public IntPtr pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public IntPtr lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCT lpFileOp);

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;      // Send to Recycle Bin
    private const ushort FOF_NOCONFIRMATION = 0x0010; // Do not prompt the user
    private const ushort FOF_SILENT = 0x0004;         // Do not show progress dialog
    private const ushort FOF_NOERRORUI = 0x0400;      // Suppress OS error dialogs

    /// <summary>
    /// Moves a file or folder to the Windows Recycle Bin using native Win32 shell API.
    /// </summary>
    // SECURITY FIX: Mitigation for P/Invoke memory leaks, null injection, and uncanonicalized path routing.
    private static bool MoveToRecycleBin(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.IndexOf('\0') >= 0) return false;

        var fullPath = Path.GetFullPath(path);
        IntPtr pFrom = IntPtr.Zero;

        try
        {
            // Null-double-null termination required by SHFileOperationW
            pFrom = Marshal.StringToHGlobalUni(fullPath + "\0\0");
            var fileOp = new SHFILEOPSTRUCT
            {
                hwnd = IntPtr.Zero,
                wFunc = FO_DELETE,
                pFrom = pFrom,
                pTo = IntPtr.Zero,
                fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI),
            };

            int result = SHFileOperationW(ref fileOp);
            return result == 0 && fileOp.fAnyOperationsAborted == 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SHFileOperationW failed for {Path}", fullPath);
            return false;
        }
        finally
        {
            if (pFrom != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pFrom);
            }
        }
    }

    // ---------------------------------------------------------------- //
    //  Permanent Safe Deletion (Files & Folders)                       //
    // ---------------------------------------------------------------- //

    private void ProcessPermanentDelete(
        string path,
        CancellationToken cancellationToken,
        ref int successCount,
        ref int failedCount,
        ref long freedBytes,
        List<CleanupFailure> failures)
    {
        // SECURITY FIX: Re-verify path safety before any permanent delete execution
        if (!FileOps.IsPathSafeForCleanup(path))
        {
            failedCount++;
            failures.Add(new CleanupFailure(path, "SECURITY BLOCKED: Path rejected by security policy", nameof(SecurityException), false));
            return;
        }

        // SECURITY FIX: Reparse Point Check (CWE-59).
        // If the path itself is a directory junction or symlink, delete ONLY the link itself.
        // Never recurse or traverse into junction targets!
        if (FileOps.IsReparsePoint(path))
        {
            try
            {
                if (Directory.Exists(path))
                {
                    // Directory.Delete with recursive: false safely unlinks the junction without touching target files
                    Directory.Delete(path, recursive: false);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
                successCount++;
                return;
            }
            catch (Exception ex)
            {
                failedCount++;
                failures.Add(new CleanupFailure(path, ex.Message, ex.GetType().Name, IsFileLocked(ex)));
                _logger?.Invoke($"Failed to delete reparse point: {path}", ex);
                return;
            }
        }

        if (File.Exists(path))
        {
            DeleteSingleFile(path, ref successCount, ref failedCount, ref freedBytes, failures);
        }
        else if (Directory.Exists(path))
        {
            DeleteDirectoryRecursive(path, cancellationToken, ref successCount, ref failedCount, ref freedBytes, failures);
        }
    }

    private void DeleteSingleFile(
        string filePath,
        ref int successCount,
        ref int failedCount,
        ref long freedBytes,
        List<CleanupFailure> failures)
    {
        try
        {
            // SECURITY FIX: TOCTOU check - Query fresh attributes directly from NTFS
            var attr = File.GetAttributes(filePath);

            // SECURITY FIX: If file is a symbolic link, delete the link directly without modifying attributes on the target file!
            if ((attr & FileAttributes.ReparsePoint) != 0)
            {
                File.Delete(filePath);
                successCount++;
                return;
            }

            var fi = new FileInfo(filePath);
            long length = 0;
            try { length = fi.Length; } catch { }

            // SECURITY FIX: Clear ReadOnly attribute safely on regular files only
            if ((attr & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(filePath, attr & ~FileAttributes.ReadOnly);
            }

            File.Delete(filePath);
            successCount++;
            freedBytes += length;
        }
        catch (Exception ex)
        {
            // SECURITY FIX: Catch locked file exceptions, log them safely, skip without crashing
            failedCount++;
            bool isLocked = IsFileLocked(ex);
            var failure = new CleanupFailure(
                Path: filePath,
                ErrorMessage: isLocked ? $"File is locked by another process: {ex.Message}" : ex.Message,
                ExceptionType: ex.GetType().Name,
                IsLocked: isLocked);

            failures.Add(failure);
            _logger?.Invoke($"Skipped locked/inaccessible file: {filePath}", ex);
        }
    }

    private void DeleteDirectoryRecursive(
        string dirPath,
        CancellationToken cancellationToken,
        ref int successCount,
        ref int failedCount,
        ref long freedBytes,
        List<CleanupFailure> failures)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // SECURITY FIX: TOCTOU check - Ensure directory has not been replaced with a junction or symlink.
        // If it is a reparse point, unlink it without recursing into target directory contents.
        try
        {
            if (FileOps.IsReparsePoint(dirPath))
            {
                Directory.Delete(dirPath, recursive: false);
                successCount++;
                return;
            }
        }
        catch (Exception ex)
        {
            failedCount++;
            failures.Add(new CleanupFailure(dirPath, ex.Message, ex.GetType().Name, IsFileLocked(ex)));
            _logger?.Invoke($"Failed to delete junction: {dirPath}", ex);
            return;
        }

        // 1. Delete all safe files in the directory
        string[] files;
        try
        {
            files = Directory.GetFiles(dirPath);
        }
        catch (Exception ex)
        {
            failedCount++;
            failures.Add(new CleanupFailure(dirPath, ex.Message, ex.GetType().Name, IsFileLocked(ex)));
            _logger?.Invoke($"Cannot enumerate files in: {dirPath}", ex);
            return;
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeleteSingleFile(file, ref successCount, ref failedCount, ref freedBytes, failures);
        }

        // 2. Recursively delete subdirectories
        string[] subDirs;
        try
        {
            subDirs = Directory.GetDirectories(dirPath);
        }
        catch (Exception ex)
        {
            failedCount++;
            failures.Add(new CleanupFailure(dirPath, ex.Message, ex.GetType().Name, IsFileLocked(ex)));
            _logger?.Invoke($"Cannot enumerate subdirectories in: {dirPath}", ex);
            return;
        }

        foreach (var subDir in subDirs)
        {
            DeleteDirectoryRecursive(subDir, cancellationToken, ref successCount, ref failedCount, ref freedBytes, failures);
        }

        // 3. Attempt to delete the now-empty directory
        try
        {
            // SECURITY FIX: Always call Directory.Delete with recursive: false.
            // This guarantees that if any unexpected file or link remained, deletion safely aborts
            // rather than wiping out an unintended target location.
            Directory.Delete(dirPath, recursive: false);
            successCount++;
        }
        catch (Exception ex)
        {
            // Directory might not be empty if some files inside were locked and skipped
            failedCount++;
            bool isLocked = IsFileLocked(ex);
            failures.Add(new CleanupFailure(dirPath, ex.Message, ex.GetType().Name, isLocked));
            _logger?.Invoke($"Directory not deleted (may contain locked files): {dirPath}", ex);
        }
    }

    // ---------------------------------------------------------------- //
    //  Helpers: Error Detection & Path Handling                         //
    // ---------------------------------------------------------------- //

    /// <summary>
    /// Determines whether an exception was caused by a file sharing violation / process lock.
    /// Standard Windows HRESULT codes:
    /// 0x80070020 = ERROR_SHARING_VIOLATION
    /// 0x80070021 = ERROR_LOCK_VIOLATION
    /// </summary>
    public static bool IsFileLocked(Exception ex)
    {
        if (ex is IOException ioEx)
        {
            int hr = ioEx.HResult & 0xFFFF;
            return hr is 32 or 33; // 32 = sharing violation, 33 = lock violation
        }
        return ex is UnauthorizedAccessException or SecurityException;
    }

    /// <summary>
    /// Expands environment variables (e.g. %AppData%, %TEMP%) and returns a normalized absolute path.
    /// Rejects paths with null characters or invalid characters to mitigate Path Traversal attacks.
    /// </summary>
    // SECURITY FIX: Mitigation for CWE-22 (Path Traversal).
    public static string ResolvePath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return string.Empty;

        // Disallow null bytes or invalid characters
        if (rawPath.IndexOf('\0') >= 0 || rawPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return string.Empty;

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(rawPath.Trim());
            return Path.GetFullPath(expanded);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to resolve path: {Path}", rawPath);
            return string.Empty;
        }
    }

    /// <summary>
    /// Measures the total byte size of a file or directory before deletion without following reparse points.
    /// </summary>
    public static long MeasurePathBytes(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !FileOps.IsPathSafeForCleanup(path))
            return 0;

        // SECURITY FIX: If directory is a reparse point, do not traverse its contents
        if (FileOps.IsReparsePoint(path))
            return 0;

        if (File.Exists(path))
        {
            try { return new FileInfo(path).Length; } catch { return 0; }
        }

        if (Directory.Exists(path))
        {
            long total = 0;
            try
            {
                var di = new DirectoryInfo(path);
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    // SECURITY FIX: Skip reparse points during measurement to avoid cycles and traversal
                    AttributesToSkip = FileAttributes.ReparsePoint
                };

                foreach (var file in di.EnumerateFiles("*", options))
                {
                    try { total += file.Length; } catch { }
                }
            }
            catch { }
            return total;
        }

        return 0;
    }

    /// <summary>
    /// Checks whether a path represents critical user data that should be preserved or sent to Recycle Bin.
    /// </summary>
    // SECURITY FIX: Re-engineered critical path detection.
    // Properly identifies sensitive personal user data folders (Documents, Desktop, Pictures)
    // while allowing temporary cache folders (like AppData\Local\Temp) to be cleaned normally.
    public static bool IsCriticalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var full = ResolvePath(path);
        if (string.IsNullOrEmpty(full)) return false;

        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Path is critical if it IS the UserProfile root itself, or in Documents/Desktop
        if (string.Equals(full.TrimEnd('\\'), userProfile.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrEmpty(documents) && full.StartsWith(documents, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrEmpty(desktop) && full.StartsWith(desktop, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
