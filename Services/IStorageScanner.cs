using System.IO;
using Pakban.Models;

namespace Pakban.Services;

/// <summary>
/// Progress reporting payload for storage scanning operations.
/// </summary>
public sealed record StorageScanProgress(
    string CurrentPath,
    string CurrentCategory,
    long ScannedBytes,
    long ScannedFiles,
    double ProgressPercentage);

/// <summary>
/// Result for a single scanned junk category on a specific drive.
/// </summary>
public sealed class ScannedJunkItem
{
    public required string Id { get; init; }
    public required string CategoryName { get; init; }
    public required string DriveLetter { get; init; }
    public required string TargetPath { get; init; }
    public long TotalBytes { get; set; }
    public long FileCount { get; set; }
}

/// <summary>
/// Complete report of a storage scan across all selected drives,
/// including standard junk and discovered leftover (orphaned) app folders.
/// </summary>
public sealed class StorageScanReport
{
    public IReadOnlyList<ScannedJunkItem> Items { get; init; } = Array.Empty<ScannedJunkItem>();
    public IReadOnlyList<OrphanItem> LeftoverFolders { get; init; } = Array.Empty<OrphanItem>();
    public long TotalBytes => Items.Sum(i => i.TotalBytes) + LeftoverFolders.Sum(o => o.Bytes);
    public long TotalFiles => Items.Sum(i => i.FileCount);
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Defines the storage scanning contract for querying drives and analyzing junk files.
/// </summary>
public interface IStorageScanner
{
    /// <summary>
    /// Dynamically discovers all ready storage drives / partitions on the system.
    /// </summary>
    IReadOnlyList<DriveInfo> GetAvailableDrives();

    /// <summary>
    /// Safely scans selected drives for known junk folders (Temp, Browser Caches, Windows Logs, etc.)
    /// without freezing the UI thread.
    /// </summary>
    Task<StorageScanReport> ScanDrivesAsync(
        IEnumerable<DriveInfo> selectedDrives,
        IProgress<StorageScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
