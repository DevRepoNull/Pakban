# 5. Core Services and API Reference (Services & API Reference)

This document provides a comprehensive technical reference for the core services, interfaces, and foundational APIs within Pakban. Developers and maintainers can reference these specifications to extend or audit the codebase.

---

## 5.1. Storage Scanner Service (`IStorageScanner` / `StorageScannerService`)

The `IStorageScanner` interface defines the operational contract for asynchronous storage enumeration and junk item discovery.

```csharp
namespace Pakban.Services;

public interface IStorageScanner
{
    IReadOnlyList<DriveInfo> GetAvailableDrives();

    Task<StorageScanReport> ScanDrivesAsync(
        IEnumerable<DriveInfo> selectedDrives,
        IProgress<StorageScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
```

### Core Methods:
- **`GetAvailableDrives()`:**  
  Enumerates all mounted fixed and removable system drives (`DriveType.Fixed`, `DriveType.Removable`). Validates drive readiness and tests partition accessibility (e.g., verifying that drives locked by BitLocker do not cause unhandled crashes).
- **`ScanDrivesAsync(...)`:**  
  Executes a non-blocking asynchronous scan across specified drives. Broadcasts real-time progress via `IProgress<StorageScanProgress>` and respects cancellation requests via `CancellationToken`.

### Data Transfer Models:
- **`StorageScanProgress`:** Record containing `CurrentPath`, `CurrentCategory`, `ScannedBytes`, `ScannedFiles`, and `ProgressPercentage`.
- **`StorageScanReport`:** Result object containing discovered `Items`, detected `LeftoverFolders`, and the total elapsed `Duration`.

---

## 5.2. File Cleanup Service (`ICleanupService` / `CleanupService`)

The `CleanupService` manages safe deletion pipelines, locked file handling, and Recycle Bin routing.

```csharp
namespace Pakban.Services;

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
```

### `CleanupReport` Properties:
- `SuccessCount`: Number of files or folders successfully deleted or recycled.
- `FailedCount`: Number of files skipped due to active sharing violations or permission blocks.
- `FreedBytes`: Net bytes recovered from disk.
- `FreedSpaceMB` / `FreedSpaceGB`: Floating-point convenience properties formatted in megabytes and gigabytes.
- `Failures`: Read-only collection of `CleanupFailure` records specifying path, exception details, and lock state.

---

## 5.3. Low-Level File Operations Guard (`FileOps`)

`FileOps` is a static utility class executing high-speed, hardened Win32 and I/O routines:

- **`IsPathSafeForCleanup(string? path)`:**  
  Sanitizes paths, validates drive root safety, verifies absence of null characters, and ensures target paths do not intersect with `ProtectedSystemRoots`.
- **`IsReparsePoint(string path)`:**  
  Queries NTFS attributes via `File.GetAttributes(path)` to verify whether an entity is a directory junction or symbolic link without following the link.
- **`MoveToRecycleBin(string path)`:**  
  P/Invokes `SHFileOperationW` with `FOF_ALLOWUNDO` to silently route paths to the Windows Recycle Bin.
- **`EmptyRecycleBin()`:**  
  Invokes `SHEmptyRecycleBinW` to purge deleted items across all mounted partitions.
- **`MeasureFolder(string dir, CancellationToken ct)`:**  
  Recursively tallies byte sizes and file counts, explicitly skipping reparse points.
- **`MeasureFiles(string dir, string pattern, CancellationToken ct)`:**  
  Measures files matching specific patterns (e.g., thumbnail cache databases).
- **`BuildCategories()`:**  
  Constructs the default registry of 13 cleanable categories, binding path providers, glyphs, and admin requirement flags.

---

## 5.4. Orphaned Data Detection (`OrphanDetection`)

The `OrphanDetection` class discovers leftover data from uninstalled software:

```csharp
public static async Task<List<OrphanItem>> FindAsync(CancellationToken ct = default);
```

### Scan Algorithm:
1. **Registered Application Discovery:**  
   Enumerates installed application subkeys across:
   - `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`
   - `HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall`
   - `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall`
   Additionally inspects shortcut `.lnk` targets across the common and user Start Menu trees.
2. **Directory Comparison:**  
   Parses top-level subfolders within `%LOCALAPPDATA%`, `%APPDATA%`, and `%PROGRAMDATA%`.
3. **Filtering:**  
   Whitelists Microsoft system directories and registered vendors. Unmatched folders are classified as `OrphanItem` candidates.

---

## 5.5. Memory Management Service (`RamService`)

`RamService` interfaces with the Windows memory manager via Win32 APIs:

- **`GetStats()`:**  
  Invokes `GlobalMemoryStatusEx` and `GetPerformanceInfo` to assemble `RamStats`:
  - `TotalGB`: Installed physical RAM.
  - `UsedGB`: Memory allocated by active processes and drivers.
  - `AvailableGB`: Memory immediately available for allocation.
  - `CachedGB`: Windows system file cache size.
  - `LoadPercent`: Total system memory pressure percentage.
- **`Optimize()`:**  
  Iterates accessible system processes, opens process handles with `PROCESS_SET_QUOTA`, and invokes `EmptyWorkingSet` (`psapi.dll`). Subsequently triggers a managed garbage collection cycle (`GC.Collect()`) within Pakban, returning an `OptimizeResult` with net memory reclaimed.

---

## 5.6. Logging Service (`LoggingService`)

- Built on **Serilog**.
- Target log directory: `%APPDATA%\Pakban\logs\app-yyyyMMdd.log`.
- Features:
  - Daily file rolling with a 30-day retention policy (`retainedFileCountLimit: 30`).
  - Structured output capturing timestamp, severity level, message, and exception stack traces.
  - Symlink attack defense (CWE-59): Refuses to append to symlinked log targets.
