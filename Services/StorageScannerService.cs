using System.Diagnostics;
using System.IO;
using Serilog;

namespace Pakban.Services;

/// <summary>
/// Production-grade implementation of <see cref="IStorageScanner"/> designed for safe,
/// non-blocking Windows storage and cache discovery.
/// Hardened against Junction Point traversal, Symlink attacks, and Path Traversal.
/// </summary>
public class StorageScannerService : IStorageScanner
{
    // SECURITY FIX: Strictly skip ReparsePoints (Junctions and Symlinks) during recursive file enumeration.
    private static readonly EnumerationOptions SafeEnumOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint // Never follow symlinks/junctions to avoid cycles and trap targets
    };

    /// <inheritdoc/>
    public IReadOnlyList<DriveInfo> GetAvailableDrives()
    {
        var readyDrives = new List<DriveInfo>();

        try
        {
            var drives = DriveInfo.GetDrives();
            foreach (var drive in drives)
            {
                try
                {
                    // Ensure the drive is mounted, ready, and accessible
                    if (drive.IsReady && (drive.DriveType is DriveType.Fixed or DriveType.Removable))
                    {
                        // Verify reading TotalSize doesn't throw (e.g. BitLocker locked or unformatted)
                        _ = drive.TotalSize;
                        readyDrives.Add(drive);
                    }
                }
                catch (Exception)
                {
                    // Drive exists in system table but is temporarily offline/unreadable
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to enumerate system drives");
        }

        return readyDrives;
    }

    /// <inheritdoc/>
    public async Task<StorageScanReport> ScanDrivesAsync(
        IEnumerable<DriveInfo> selectedDrives,
        IProgress<StorageScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectedDrives);

        var drivesList = selectedDrives.ToList();
        if (drivesList.Count == 0)
        {
            return new StorageScanReport { Items = Array.Empty<ScannedJunkItem>(), Duration = TimeSpan.Zero };
        }

        return await Task.Run(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            var results = new List<ScannedJunkItem>();

            var targets = BuildJunkTargets(drivesList);
            int totalTargets = targets.Count;
            int currentTargetIndex = 0;

            long totalAccumulatedBytes = 0;
            long totalAccumulatedFiles = 0;

            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                currentTargetIndex++;

                double percentage = totalTargets > 0
                    ? ((double)currentTargetIndex / totalTargets) * 100.0
                    : 0;

                progress?.Report(new StorageScanProgress(
                    CurrentPath: target.DirectoryPath,
                    CurrentCategory: target.CategoryName,
                    ScannedBytes: totalAccumulatedBytes,
                    ScannedFiles: totalAccumulatedFiles,
                    ProgressPercentage: percentage));

                // SECURITY FIX: Path validation & Reparse Point check before scanning
                if (!Directory.Exists(target.DirectoryPath) ||
                    !FileOps.IsPathSafeForCleanup(target.DirectoryPath) ||
                    FileOps.IsReparsePoint(target.DirectoryPath))
                {
                    continue;
                }

                var (bytes, files) = MeasureDirectorySafely(
                    target.DirectoryPath,
                    target.SearchPattern,
                    progress,
                    ref totalAccumulatedBytes,
                    ref totalAccumulatedFiles,
                    target.CategoryName,
                    percentage,
                    cancellationToken);

                if (bytes > 0 || files > 0)
                {
                    results.Add(new ScannedJunkItem
                    {
                        Id = target.Id,
                        CategoryName = target.CategoryName,
                        DriveLetter = target.DriveLetter,
                        TargetPath = target.DirectoryPath,
                        TotalBytes = bytes,
                        FileCount = files
                    });
                }
            }

            // Leftover (Orphaned) app data detection on selected drives
            var leftoverFolders = new List<Pakban.Models.OrphanItem>();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new StorageScanProgress(
                    CurrentPath: "Analyzing leftover application data...",
                    CurrentCategory: "Leftover Data",
                    ScannedBytes: totalAccumulatedBytes,
                    ScannedFiles: totalAccumulatedFiles,
                    ProgressPercentage: 92.0));

                var allOrphans = OrphanDetection.FindAsync(cancellationToken).GetAwaiter().GetResult();
                var selectedRoots = drivesList.Select(d => d.RootDirectory.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var orphan in allOrphans)
                {
                    var root = Path.GetPathRoot(orphan.Path);
                    // SECURITY FIX: Verify path safety on each orphan path
                    if (root != null && selectedRoots.Contains(root) && FileOps.IsPathSafeForCleanup(orphan.Path))
                    {
                        leftoverFolders.Add(orphan);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Orphan detection fallback error during scan");
            }

            stopwatch.Stop();

            progress?.Report(new StorageScanProgress(
                CurrentPath: "Scan completed",
                CurrentCategory: "Done",
                ScannedBytes: totalAccumulatedBytes,
                ScannedFiles: totalAccumulatedFiles,
                ProgressPercentage: 100.0));

            return new StorageScanReport
            {
                Items = results,
                LeftoverFolders = leftoverFolders,
                Duration = stopwatch.Elapsed
            };
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Safely calculates total size and file count inside a folder while gracefully ignoring
    /// in-use, locked, or protected operating system files.
    /// Hardened against Reparse Points / Junctions.
    /// </summary>
    private static (long Bytes, long Files) MeasureDirectorySafely(
        string directoryPath,
        string searchPattern,
        IProgress<StorageScanProgress>? progress,
        ref long totalAccumulatedBytes,
        ref long totalAccumulatedFiles,
        string categoryName,
        double currentPercentage,
        CancellationToken cancellationToken)
    {
        long dirBytes = 0;
        long dirFiles = 0;
        int fileBatchCounter = 0;

        try
        {
            // SECURITY FIX: Path safety & Reparse Point Check on the folder itself before creating DirectoryInfo
            if (!FileOps.IsPathSafeForCleanup(directoryPath) || FileOps.IsReparsePoint(directoryPath))
            {
                Log.Warning("SECURITY FIX: Skipping measurement of unsafe or reparse-point directory: {Path}", directoryPath);
                return (0, 0);
            }

            var dirInfo = new DirectoryInfo(directoryPath);
            if (!dirInfo.Exists) return (0, 0);

            // Enumerate files safely using non-allocating enumerable and ignoring unauthorized files
            var files = dirInfo.EnumerateFiles(searchPattern, SafeEnumOptions);

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // SECURITY FIX: Ensure file itself is not a reparse point (symlink)
                    if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                        continue;

                    long length = file.Length;
                    dirBytes += length;
                    dirFiles++;
                    totalAccumulatedBytes += length;
                    totalAccumulatedFiles++;
                    fileBatchCounter++;

                    // Throttle progress dispatches to avoid UI thread flooding (every 150 files)
                    if (fileBatchCounter % 150 == 0)
                    {
                        progress?.Report(new StorageScanProgress(
                            CurrentPath: file.FullName,
                            CurrentCategory: categoryName,
                            ScannedBytes: totalAccumulatedBytes,
                            ScannedFiles: totalAccumulatedFiles,
                            ProgressPercentage: currentPercentage));
                    }
                }
                catch (Exception)
                {
                    // Skip files that are locked by running processes or have revoked permissions
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Directory measurement skipped or inaccessible: {Path}", directoryPath);
        }

        return (dirBytes, dirFiles);
    }

    /// <summary>
    /// Maps high-impact junk locations to their respective drive roots.
    /// Only folders on the user-selected drives will be generated and inspected.
    /// </summary>
    private static List<JunkScanTarget> BuildJunkTargets(IEnumerable<DriveInfo> drives)
    {
        var targets = new List<JunkScanTarget>();

        foreach (var drive in drives)
        {
            string root = drive.RootDirectory.FullName; // e.g. "C:\"
            string driveLetter = drive.Name.TrimEnd('\\');

            // 1. Recycle Bin per selected drive
            targets.Add(new JunkScanTarget(
                Id: $"recycle_{driveLetter}",
                CategoryName: $"Recycle Bin ({driveLetter})",
                DriveLetter: driveLetter,
                DirectoryPath: Path.Combine(root, "$Recycle.Bin")));

            // 2. Drive root Temporary folders
            targets.Add(new JunkScanTarget(
                Id: $"root_temp_{driveLetter}",
                CategoryName: $"Root Temp ({driveLetter})",
                DriveLetter: driveLetter,
                DirectoryPath: Path.Combine(root, "Temp")));

            targets.Add(new JunkScanTarget(
                Id: $"root_tmp_{driveLetter}",
                CategoryName: $"Root Tmp ({driveLetter})",
                DriveLetter: driveLetter,
                DirectoryPath: Path.Combine(root, "tmp")));

            // 3. Windows Upgrade leftovers (Windows.old, $Windows.~BT, etc.)
            targets.Add(new JunkScanTarget(
                Id: $"win_old_{driveLetter}",
                CategoryName: $"Previous Windows Installation ({driveLetter})",
                DriveLetter: driveLetter,
                DirectoryPath: Path.Combine(root, "Windows.old")));

            targets.Add(new JunkScanTarget(
                Id: $"win_bt_{driveLetter}",
                CategoryName: $"Windows Setup Temp ({driveLetter})",
                DriveLetter: driveLetter,
                DirectoryPath: Path.Combine(root, "$Windows.~BT")));

            targets.Add(new JunkScanTarget(
                Id: $"win_ws_{driveLetter}",
                CategoryName: $"Windows WS Temp ({driveLetter})",
                DriveLetter: driveLetter,
                DirectoryPath: Path.Combine(root, "$Windows.~WS")));

            // 4. Windows System Junk (if Windows OS is installed on this drive)
            string winDir = Path.Combine(root, "Windows");
            if (Directory.Exists(winDir) && !FileOps.IsReparsePoint(winDir))
            {
                targets.Add(new JunkScanTarget(
                    Id: $"win_temp_{driveLetter}",
                    CategoryName: "Windows Temp",
                    DriveLetter: driveLetter,
                    DirectoryPath: Path.Combine(winDir, "Temp")));

                targets.Add(new JunkScanTarget(
                    Id: $"win_softwaredist_{driveLetter}",
                    CategoryName: "Windows Update Download Cache",
                    DriveLetter: driveLetter,
                    DirectoryPath: Path.Combine(winDir, "SoftwareDistribution", "Download")));

                targets.Add(new JunkScanTarget(
                    Id: $"win_logs_{driveLetter}",
                    CategoryName: "Windows System Logs",
                    DriveLetter: driveLetter,
                    DirectoryPath: Path.Combine(winDir, "Logs")));

                targets.Add(new JunkScanTarget(
                    Id: $"win_prefetch_{driveLetter}",
                    CategoryName: "Windows Prefetch Cache",
                    DriveLetter: driveLetter,
                    DirectoryPath: Path.Combine(winDir, "Prefetch")));

                targets.Add(new JunkScanTarget(
                    Id: $"win_minidump_{driveLetter}",
                    CategoryName: "System Crash Minidumps",
                    DriveLetter: driveLetter,
                    DirectoryPath: Path.Combine(winDir, "Minidump")));

                targets.Add(new JunkScanTarget(
                    Id: $"win_delivery_{driveLetter}",
                    CategoryName: "Delivery Optimization Cache",
                    DriveLetter: driveLetter,
                    DirectoryPath: Path.Combine(winDir, "ServiceProfiles", "NetworkService",
                        "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "Cache")));
            }

            // 5. User Temp & Application Caches (Browser, Thumbnails, etc.)
            string usersDir = Path.Combine(root, "Users");
            if (Directory.Exists(usersDir) && !FileOps.IsReparsePoint(usersDir))
            {
                try
                {
                    var userProfiles = Directory.EnumerateDirectories(usersDir);
                    foreach (var userFolder in userProfiles)
                    {
                        // SECURITY FIX: Skip user profile folder if it is a reparse point or junction
                        if (FileOps.IsReparsePoint(userFolder)) continue;

                        string userName = Path.GetFileName(userFolder);
                        if (userName.Equals("Public", StringComparison.OrdinalIgnoreCase) ||
                            userName.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                            userName.Equals("All Users", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string localAppData = Path.Combine(userFolder, "AppData", "Local");
                        string roamingAppData = Path.Combine(userFolder, "AppData", "Roaming");

                        if (!Directory.Exists(localAppData) || FileOps.IsReparsePoint(localAppData)) continue;

                        // User Temp
                        targets.Add(new JunkScanTarget(
                            Id: $"user_temp_{userName}",
                            CategoryName: $"User Temp ({userName})",
                            DriveLetter: driveLetter,
                            DirectoryPath: Path.Combine(localAppData, "Temp")));

                        // Crash dumps
                        targets.Add(new JunkScanTarget(
                            Id: $"user_dumps_{userName}",
                            CategoryName: $"User Crash Dumps ({userName})",
                            DriveLetter: driveLetter,
                            DirectoryPath: Path.Combine(localAppData, "CrashDumps")));

                        // Windows Thumbnail cache
                        targets.Add(new JunkScanTarget(
                            Id: $"user_thumbs_{userName}",
                            CategoryName: $"Thumbnail Cache ({userName})",
                            DriveLetter: driveLetter,
                            DirectoryPath: Path.Combine(localAppData, "Microsoft", "Windows", "Explorer"),
                            SearchPattern: "*thumbcache_*.db"));

                        // Browser Caches
                        AddBrowserCacheTargets(targets, driveLetter, userName, localAppData, roamingAppData);

                        // GPU & DirectX Shader Caches
                        targets.Add(new JunkScanTarget(
                            Id: $"user_d3d_{userName}",
                            CategoryName: $"DirectX Shader Cache ({userName})",
                            DriveLetter: driveLetter,
                            DirectoryPath: Path.Combine(localAppData, "D3DSCache")));

                        targets.Add(new JunkScanTarget(
                            Id: $"user_nvidia_{userName}",
                            CategoryName: $"NVIDIA GPU Cache ({userName})",
                            DriveLetter: driveLetter,
                            DirectoryPath: Path.Combine(localAppData, "NVIDIA", "GLCache")));

                        // Developer package caches
                        targets.Add(new JunkScanTarget(
                            Id: $"nuget_cache_{userName}",
                            CategoryName: $"NuGet Package Cache ({userName})",
                            DriveLetter: driveLetter,
                            DirectoryPath: Path.Combine(localAppData, "NuGet", "v3-cache")));

                        targets.Add(new JunkScanTarget(
                            Id: $"npm_cache_{userName}",
                            CategoryName: $"NPM Cache ({userName})",
                            DriveLetter: driveLetter,
                            DirectoryPath: Path.Combine(roamingAppData, "npm-cache")));
                    }
                }
                catch (Exception)
                {
                    // Access denied to Users directory or specific profile folder
                }
            }
        }

        return targets;
    }

    private static void AddBrowserCacheTargets(
        List<JunkScanTarget> targets,
        string driveLetter,
        string userName,
        string localAppData,
        string roamingAppData)
    {
        var chromiumBrowsers = new (string BrowserName, string RelativeUserData)[]
        {
            ("Google Chrome", Path.Combine("Google", "Chrome", "User Data")),
            ("Microsoft Edge", Path.Combine("Microsoft", "Edge", "User Data")),
            ("Brave Browser", Path.Combine("BraveSoftware", "Brave-Browser", "User Data")),
            ("Vivaldi", Path.Combine("Vivaldi", "User Data"))
        };

        foreach (var (bName, relPath) in chromiumBrowsers)
        {
            string userData = Path.Combine(localAppData, relPath);
            // SECURITY FIX: Skip browser path if nonexistent or a reparse point
            if (!Directory.Exists(userData) || FileOps.IsReparsePoint(userData)) continue;

            targets.Add(new JunkScanTarget(
                Id: $"{bName.ToLowerInvariant()}_cache_{userName}",
                CategoryName: $"{bName} Cache ({userName})",
                DriveLetter: driveLetter,
                DirectoryPath: Path.Combine(userData, "Default", "Cache")));

            targets.Add(new JunkScanTarget(
                Id: $"{bName.ToLowerInvariant()}_codecache_{userName}",
                CategoryName: $"{bName} Code Cache ({userName})",
                DriveLetter: driveLetter,
                DirectoryPath: Path.Combine(userData, "Default", "Code Cache")));

            targets.Add(new JunkScanTarget(
                Id: $"{bName.ToLowerInvariant()}_gpucache_{userName}",
                CategoryName: $"{bName} GPU Cache ({userName})",
                DriveLetter: driveLetter,
                DirectoryPath: Path.Combine(userData, "Default", "GPUCache")));
        }

        // Mozilla Firefox
        string firefoxProfiles = Path.Combine(localAppData, "Mozilla", "Firefox", "Profiles");
        if (Directory.Exists(firefoxProfiles) && !FileOps.IsReparsePoint(firefoxProfiles))
        {
            try
            {
                foreach (var profile in Directory.EnumerateDirectories(firefoxProfiles))
                {
                    if (FileOps.IsReparsePoint(profile)) continue;
                    targets.Add(new JunkScanTarget(
                        Id: $"firefox_cache_{userName}_{Path.GetFileName(profile)}",
                        CategoryName: $"Firefox Cache ({userName})",
                        DriveLetter: driveLetter,
                        DirectoryPath: Path.Combine(profile, "cache2")));
                }
            }
            catch (Exception) { }
        }

        // Opera
        string opera = Path.Combine(localAppData, "Opera Software");
        if (Directory.Exists(opera) && !FileOps.IsReparsePoint(opera))
        {
            targets.Add(new JunkScanTarget(
                Id: $"opera_cache_{userName}",
                CategoryName: $"Opera Cache ({userName})",
                DriveLetter: driveLetter,
                DirectoryPath: Path.Combine(opera, "Opera Stable", "Cache")));
        }
    }

    private sealed record JunkScanTarget(
        string Id,
        string CategoryName,
        string DriveLetter,
        string DirectoryPath,
        string SearchPattern = "*");
}
