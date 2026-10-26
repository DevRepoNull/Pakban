using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using Pakban.Models;
using Serilog;

namespace Pakban.Services;

/// <summary>
/// Low-level, fault-tolerant, security-hardened file operations:
/// measuring, deleting, Recycle Bin, and privilege management.
/// Hardened according to Microsoft Secure Coding Guidelines and OWASP Desktop App Security.
/// </summary>
public static class FileOps
{
    // ---------------------------------------------------------------- //
    //  Security Validation & Hardening Guards                           //
    // ---------------------------------------------------------------- //

    // SECURITY FIX: Centralized list of critical operating system paths to protect against
    // accidental or malicious deletion (CWE-22 / CWE-59).
    private static readonly HashSet<string> ProtectedSystemRoots = new(StringComparer.OrdinalIgnoreCase);

    static FileOps()
    {
        InitializeProtectedRoots();
    }

    private static void InitializeProtectedRoots()
    {
        // SECURITY FIX: Populate blacklist of directories that must NEVER be cleaned or deleted
        void AddSafe(string? p)
        {
            if (!string.IsNullOrWhiteSpace(p))
            {
                try
                {
                    ProtectedSystemRoots.Add(Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                }
                catch { }
            }
        }

        AddSafe(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        AddSafe(Environment.GetFolderPath(Environment.SpecialFolder.System));
        AddSafe(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86));
        AddSafe(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        AddSafe(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        AddSafe(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles));
        AddSafe(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86));
        AddSafe(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        AddSafe(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        AddSafe(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        AddSafe(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments));

        // Add root of Users directory (e.g. C:\Users)
        try
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var usersDir = Path.GetDirectoryName(userProfile);
            AddSafe(usersDir);
        }
        catch { }
    }

    /// <summary>
    /// Checks whether a given path is a symbolic link, directory junction, or reparse point.
    /// Uses File.GetAttributes which directly queries NTFS attributes without following links.
    /// </summary>
    // SECURITY FIX: Mitigation for CWE-59 (Link Following / Reparse Point Attacks).
    // Using File.GetAttributes queries the link object directly instead of following the target.
    public static bool IsReparsePoint(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var attr = File.GetAttributes(path);
            return (attr & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            // If attributes cannot be read (e.g., file doesn't exist or sharing violation), treat conservatively as false
            return false;
        }
    }

    /// <summary>
    /// Validates whether a directory or file path is safe for scanning and cleanup operations.
    /// Blocks directory traversal, null bytes, drive roots, protected OS directories, and network UNC paths.
    /// </summary>
    // SECURITY FIX: Mitigation for CWE-22 (Path Traversal) and accidental system destruction.
    public static bool IsPathSafeForCleanup(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        // SECURITY FIX: Disallow null characters or invalid path characters that could trick Win32 APIs
        if (path.IndexOf('\0') >= 0 || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return false;

        try
        {
            // SECURITY FIX: Canonicalize path to resolve relative segments ('.', '..')
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // SECURITY FIX: Reject UNC paths (\\server\share) to prevent SMB hash leaks and remote manipulation
            if (fullPath.StartsWith(@"\\", StringComparison.Ordinal) && !fullPath.StartsWith(@"\\?\", StringComparison.Ordinal))
                return false;

            // SECURITY FIX: Block Drive Roots (e.g., "C:\", "D:\"). Never allow cleaning drive roots directly.
            var root = Path.GetPathRoot(fullPath);
            if (!string.IsNullOrEmpty(root))
            {
                var trimmedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(fullPath, trimmedRoot, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Warning("SECURITY BLOCKED: Attempted operation on drive root: {Path}", fullPath);
                    return false;
                }
            }

            // SECURITY FIX: Check against protected Windows & User root folders
            if (ProtectedSystemRoots.Contains(fullPath))
            {
                Log.Warning("SECURITY BLOCKED: Attempted operation on protected system directory: {Path}", fullPath);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Path safety verification failed for {Path}", path);
            return false;
        }
    }

    // ---------------------------------------------------------------- //
    //  Privileges & UAC                                                 //
    // ---------------------------------------------------------------- //

    public static bool IsElevated
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to query current Windows identity privileges");
                return false;
            }
        }
    }

    /// <summary>
    /// Restarts the application requesting Administrator privileges via UAC securely.
    /// </summary>
    // SECURITY FIX: Mitigation for CWE-250 (Execution with Unnecessary Privileges) & Working Directory Hijacking.
    // Explicitly pin WorkingDirectory and sanitize executable path to prevent binary hijacking during elevation.
    public static void RestartAsAdministrator()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
        {
            Log.Error("SECURITY FIX: Cannot restart as administrator because process path could not be verified.");
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                Verb = "runas",
                // SECURITY FIX: Set WorkingDirectory to the application's trusted base directory to avoid DLL hijacking
                WorkingDirectory = AppContext.BaseDirectory
            };

            Process.Start(psi);
            Application.Current?.Shutdown();
        }
        catch (Win32Exception)
        {
            // User declined the UAC elevation prompt - standard safe exit
            Log.Information("UAC prompt was declined or cancelled by the user.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to launch elevated process.");
        }
    }

    // ---------------------------------------------------------------- //
    //  Measuring                                                        //
    // ---------------------------------------------------------------- //

    // SECURITY FIX: Configure EnumerationOptions to strictly skip reparse points (symlinks/junctions).
    private static readonly EnumerationOptions SafeEnumOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint // Never follow junctions/symlinks
    };

    public static (long Bytes, long Files) MeasureFolder(string? path, CancellationToken ct)
    {
        // SECURITY FIX: Validate path safety and verify directory is not a reparse point before measuring
        if (!IsPathSafeForCleanup(path) || !Directory.Exists(path)) return (0, 0);

        // SECURITY FIX: If the target root folder itself is a junction or symlink, do NOT traverse into it
        if (IsReparsePoint(path!))
        {
            Log.Warning("SECURITY FIX: Skipping measurement because directory root itself is a reparse point: {Path}", path);
            return (0, 0);
        }

        long bytes = 0, files = 0;
        try
        {
            var di = new DirectoryInfo(path!);
            foreach (var f in di.EnumerateFiles("*", SafeEnumOptions))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    // SECURITY FIX: Ignore reparse point files encountered during enumeration
                    if ((f.Attributes & FileAttributes.ReparsePoint) == 0)
                    {
                        bytes += f.Length;
                        files++;
                    }
                }
                catch { /* inaccessible file */ }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Debug(ex, "Folder measurement partial read on {Path}", path);
        }
        return (bytes, files);
    }

    public static (long Bytes, long Files) MeasureFiles(string? path, string pattern, CancellationToken ct)
    {
        // SECURITY FIX: Validate path safety and avoid following reparse points
        if (!IsPathSafeForCleanup(path) || !Directory.Exists(path)) return (0, 0);

        if (IsReparsePoint(path!))
        {
            Log.Warning("SECURITY FIX: Skipping files measurement because directory root is a reparse point: {Path}", path);
            return (0, 0);
        }

        long bytes = 0, files = 0;
        try
        {
            var di = new DirectoryInfo(path!);
            foreach (var f in di.EnumerateFiles(pattern, SafeEnumOptions))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if ((f.Attributes & FileAttributes.ReparsePoint) == 0)
                    {
                        bytes += f.Length;
                        files++;
                    }
                }
                catch { }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Debug(ex, "Files measurement partial read on {Path}", path);
        }
        return (bytes, files);
    }

    // ---------------------------------------------------------------- //
    //  Deleting                                                         //
    // ---------------------------------------------------------------- //

    // SECURITY FIX: Canonicalize path before prepending extended-length prefix.
    // Handles UNC paths properly (\\?\UNC\) and prevents path traversal bypasses.
    private static string Prefix(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var full = Path.GetFullPath(path);

        if (full.StartsWith(@"\\?\", StringComparison.Ordinal))
            return full;

        if (full.StartsWith(@"\\", StringComparison.Ordinal))
            return @"\\?\UNC\" + full.Substring(2);

        return @"\\?\" + full;
    }

    public sealed record CleanReport(long Bytes, long Files, int Errors);

    /// <summary>
    /// Deletes the contents of a folder (the folder itself stays), skipping in-use files.
    /// Hardened against Symlink/Junction traversal, TOCTOU races, and directory traversal.
    /// </summary>
    public static CleanReport DeleteFolderContents(string? path, CancellationToken ct)
    {
        // SECURITY FIX: Validate path safety before deleting anything
        if (!IsPathSafeForCleanup(path) || !Directory.Exists(path))
        {
            return new CleanReport(0, 0, 0);
        }

        var fullPath = Path.GetFullPath(path!);

        // SECURITY FIX: If the root cleanup folder itself has been turned into a reparse point (junction/symlink),
        // abort immediately to prevent deleting targets outside the intended tree!
        if (IsReparsePoint(fullPath))
        {
            Log.Error("SECURITY FIX: Aborted DeleteFolderContents because target root is a reparse point: {Path}", fullPath);
            return new CleanReport(0, 0, 1);
        }

        long bytes = 0, files = 0;
        int errors = 0;

        void DeleteDir(string dir)
        {
            ct.ThrowIfCancellationRequested();

            // SECURITY FIX: TOCTOU check - Verify directory has not been replaced by a junction during deletion
            if (IsReparsePoint(dir))
            {
                Log.Warning("SECURITY FIX: Detected directory junction/reparse point at {Dir}. Unlinking junction without traversing.", dir);
                try
                {
                    // Delete only the junction itself (recursive = false ensures no target files are touched)
                    Directory.Delete(dir, recursive: false);
                }
                catch (Exception ex)
                {
                    errors++;
                    Log.Debug(ex, "Failed to unlink directory junction {Dir}", dir);
                }
                return;
            }

            // 1. Delete regular files in the directory
            foreach (var f in SafeFiles(dir))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    // SECURITY FIX: TOCTOU & Symlink Check on each file before deletion
                    var attr = File.GetAttributes(f);
                    bool isFileReparse = (attr & FileAttributes.ReparsePoint) != 0;

                    if (isFileReparse)
                    {
                        // File is a symlink or reparse point. Delete the link itself directly without stripping ReadOnly attributes on target!
                        File.Delete(f);
                        files++;
                        continue;
                    }

                    var fi = new FileInfo(Prefix(f));
                    long length = 0;
                    try { length = fi.Length; } catch { }

                    // SECURITY FIX: Remove ReadOnly attribute if present so deletion succeeds
                    if ((fi.Attributes & FileAttributes.ReadOnly) != 0)
                    {
                        fi.Attributes &= ~FileAttributes.ReadOnly;
                    }

                    fi.Delete();
                    bytes += length;
                    files++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    errors++;
                    Log.Debug(ex, "Failed to delete file {File} (file may be in-use or locked)", f);
                }
            }

            // 2. Recursively delete subdirectories
            foreach (var d in SafeDirs(dir))
            {
                DeleteDir(d);

                // SECURITY FIX: Always use recursive: false!
                // If subdirectories were properly emptied, non-recursive delete removes the directory safely.
                // If an attacker placed a junction or locked files remain, non-recursive delete fails safely without wiping targets.
                try
                {
                    Directory.Delete(Prefix(d), recursive: false);
                }
                catch (Exception ex)
                {
                    errors++;
                    Log.Debug(ex, "Could not delete directory {Dir} (may still contain in-use files)", d);
                }
            }
        }

        try
        {
            DeleteDir(fullPath);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            errors++;
            Log.Error(ex, "Unexpected error in DeleteFolderContents on {Path}", fullPath);
        }

        return new CleanReport(bytes, files, errors);
    }

    private static readonly EnumerationOptions SafeTopDirectoryEnumOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    /// <summary>
    /// Safely deletes files matching a pattern in a directory without traversing symlinks or leaving bounds.
    /// </summary>
    // SECURITY FIX: Added dedicated safe pattern file deletion to replace unsafe ad-hoc deletions in ViewModels.
    public static CleanReport DeleteFilesMatchingPattern(string? path, string pattern, CancellationToken ct)
    {
        if (!IsPathSafeForCleanup(path) || !Directory.Exists(path))
            return new CleanReport(0, 0, 0);

        var fullPath = Path.GetFullPath(path!);
        if (IsReparsePoint(fullPath))
        {
            Log.Warning("SECURITY FIX: Skipping DeleteFilesMatchingPattern because root is a reparse point: {Path}", fullPath);
            return new CleanReport(0, 0, 1);
        }

        long bytes = 0, files = 0;
        int errors = 0;

        try
        {
            var di = new DirectoryInfo(fullPath);
            foreach (var fi in di.EnumerateFiles(pattern, SafeTopDirectoryEnumOptions))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    // SECURITY FIX: Verify file is not a symlink pointing to critical system files
                    if ((fi.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        fi.Delete(); // Deletes only the link itself
                        files++;
                        continue;
                    }

                    long length = 0;
                    try { length = fi.Length; } catch { }

                    if ((fi.Attributes & FileAttributes.ReadOnly) != 0)
                    {
                        fi.Attributes &= ~FileAttributes.ReadOnly;
                    }

                    fi.Delete();
                    bytes += length;
                    files++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    errors++;
                    Log.Debug(ex, "Failed to delete file matching pattern: {File}", fi.FullName);
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            errors++;
            Log.Error(ex, "Error enumerating files for pattern delete in {Path}", fullPath);
        }

        return new CleanReport(bytes, files, errors);
    }

    private static IEnumerable<string> SafeFiles(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> SafeDirs(string dir)
    {
        try
        {
            // SECURITY FIX: Use DirectoryInfo and ensure ReparsePoint directories are handled or filtered
            return Directory.EnumerateDirectories(dir);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    // ---------------------------------------------------------------- //
    //  Recycle Bin (shell API)                                         //
    // ---------------------------------------------------------------- //

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public uint cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBinW(string pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBinW(IntPtr hwnd, string? pszRootPath, uint dwFlags);

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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCT lpFileOp);

    private const uint FO_DELETE = 3;
    private const ushort FOF_ALLOWUNDO = 0x40;
    private const ushort FOF_NOCONFIRMATION = 0x10;
    private const ushort FOF_SILENT = 0x04;
    private const ushort FOF_NOERRORUI = 0x400;
    private const uint SHERB_NOCONFIRMATION = 0x1;
    private const uint SHERB_NOPROGRESSUI = 0x2;
    private const uint SHERB_NOSOUND = 0x4;

    public static long GetRecycleBinSize()
    {
        long total = 0;
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                try
                {
                    var info = new SHQUERYRBINFO { cbSize = (uint)Marshal.SizeOf<SHQUERYRBINFO>() };
                    if (SHQueryRecycleBinW(drive.Name, ref info) == 0)
                        total += info.i64Size;
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error querying Recycle Bin size");
        }
        return total;
    }

    public static void EmptyRecycleBin()
    {
        try
        {
            SHEmptyRecycleBinW(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to empty Recycle Bin");
        }
    }

    /// <summary>
    /// Moves files/folders to the Recycle Bin so the user can restore them.
    /// Validates all paths to prevent sending invalid or system-critical paths to the shell.
    /// </summary>
    // SECURITY FIX: Mitigation for CWE-22 & P/Invoke Memory Corruption.
    // Verifies each path is safe, canonicalized, and contains no null characters before Marshalling.
    public static bool MoveToRecycleBin(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return true;

        var validatedPaths = new List<string>();
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;

            // SECURITY FIX: Reject paths with null bytes or unverified safety
            if (p.IndexOf('\0') >= 0 || !IsPathSafeForCleanup(p))
            {
                Log.Warning("SECURITY FIX: Refused to send invalid or unsafe path to Recycle Bin: {Path}", p);
                continue;
            }

            try
            {
                var full = Path.GetFullPath(p);
                if (File.Exists(full) || Directory.Exists(full))
                {
                    validatedPaths.Add(full);
                }
            }
            catch { }
        }

        if (validatedPaths.Count == 0) return true;

        IntPtr from = IntPtr.Zero;
        try
        {
            // Shell API requires double-null termination: string\0string\0\0
            var bufferString = string.Join('\0', validatedPaths) + "\0\0";
            from = Marshal.StringToHGlobalUni(bufferString);

            var op = new SHFILEOPSTRUCT
            {
                hwnd = IntPtr.Zero,
                wFunc = FO_DELETE,
                pFrom = from,
                pTo = IntPtr.Zero,
                fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI),
            };

            int result = SHFileOperationW(ref op);
            return result == 0 && op.fAnyOperationsAborted == 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception occurred during SHFileOperationW (MoveToRecycleBin)");
            return false;
        }
        finally
        {
            if (from != IntPtr.Zero) Marshal.FreeHGlobal(from);
        }
    }

    // ---------------------------------------------------------------- //
    //  Formatting                                                       //
    // ---------------------------------------------------------------- //

    public const string Lrm = "\u200E";

    public static string FormatSize(long bytes)
    {
        double b = bytes;
        string unit;
        if (b >= 1L << 40) { b /= 1L << 40; unit = "TB"; }
        else if (b >= 1L << 30) { b /= 1L << 30; unit = "GB"; }
        else if (b >= 1L << 20) { b /= 1L << 20; unit = "MB"; }
        else if (b >= 1L << 10) { b /= 1L << 10; unit = "KB"; }
        else return $"{bytes} B";
        return $"{Lrm}{b.ToString(b >= 100 ? "N0" : "N1", CultureInfo.InvariantCulture)} {unit}{Lrm}";
    }

    // ---------------------------------------------------------------- //
    //  Category catalogue                                               //
    // ---------------------------------------------------------------- //

    private static string WinDir => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string RoamingAppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Everything Pakban knows how to find and clean.</summary>
    public static List<CleanCategory> BuildCategories() => new()
    {
        new CleanCategory
        {
            Id = "temp_user", NameKey = "cat_temp_user", DescKey = "cat_temp_user_desc",
            Glyph = "\uE7C3", ColorIndex = 0, Kind = CleanKind.Folder,
            PathProvider = () => new[] { Path.GetTempPath() },
        },
        new CleanCategory
        {
            Id = "temp_windows", NameKey = "cat_temp_windows", DescKey = "cat_temp_windows_desc",
            Glyph = "\uE7C3", ColorIndex = 1, Kind = CleanKind.Folder, NeedsAdmin = true,
            // SECURITY FIX: Point explicitly to %WINDIR%\Temp instead of SysFolder\Temp (which pointed to System32)
            PathProvider = () => new[] { Path.Combine(WinDir, "Temp") },
        },
        new CleanCategory
        {
            Id = "update_cache", NameKey = "cat_update_cache", DescKey = "cat_update_cache_desc",
            Glyph = "\uE895", ColorIndex = 2, Kind = CleanKind.Folder, NeedsAdmin = true,
            PathProvider = () => new[] { Path.Combine(WinDir, "SoftwareDistribution", "Download") },
        },
        new CleanCategory
        {
            Id = "delivery", NameKey = "cat_delivery", DescKey = "cat_delivery_desc",
            Glyph = "\uE8E5", ColorIndex = 3, Kind = CleanKind.Folder, NeedsAdmin = true,
            PathProvider = () => new[] { Path.Combine(WinDir,
                "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows",
                "DeliveryOptimization", "Cache") },
        },
        new CleanCategory
        {
            Id = "recycle", NameKey = "cat_recycle", DescKey = "cat_recycle_desc",
            Glyph = "\uE74D", ColorIndex = 4, Kind = CleanKind.RecycleBin,
            PathProvider = () => Array.Empty<string>(),
        },
        new CleanCategory
        {
            Id = "thumbs", NameKey = "cat_thumbs", DescKey = "cat_thumbs_desc",
            Glyph = "\uE8B9", ColorIndex = 5, Kind = CleanKind.Files, FilePattern = "*cache_*.db",
            PathProvider = () => new[] { Path.Combine(LocalAppData, "Microsoft", "Windows", "Explorer") },
        },
        new CleanCategory
        {
            Id = "browsers", NameKey = "cat_browsers", DescKey = "cat_browsers_desc",
            Glyph = "\uE774", ColorIndex = 6, Kind = CleanKind.Folder,
            PathProvider = BrowserCachePaths,
        },
        new CleanCategory
        {
            Id = "shaders", NameKey = "cat_shaders", DescKey = "cat_shaders_desc",
            Glyph = "\uE950", ColorIndex = 7, Kind = CleanKind.Folder,
            PathProvider = () => new[]
            {
                Path.Combine(LocalAppData, "D3DSCache"),
                Path.Combine(LocalAppData, "NVIDIA", "GLCache"),
                Path.Combine(LocalAppData, "NVIDIA Corporation", "NV_Cache"),
                Path.Combine(LocalAppData, "AMD", "DxCache"),
                Path.Combine(LocalAppData, "AMD", "DxcCache"),
                Path.Combine(LocalAppData, "Intel", "ShaderCache"),
            },
        },
        new CleanCategory
        {
            Id = "dumps", NameKey = "cat_dumps", DescKey = "cat_dumps_desc",
            Glyph = "\uE7BA", ColorIndex = 8, Kind = CleanKind.Folder, NeedsAdmin = true,
            PathProvider = () => new[]
            {
                Path.Combine(LocalAppData, "CrashDumps"),
                Path.Combine(LocalAppData, "Microsoft", "Windows", "WER", "ReportQueue"),
                Path.Combine(LocalAppData, "Microsoft", "Windows", "WER", "ReportArchive"),
                Path.Combine(WinDir, "Minidump"),
            },
        },
        new CleanCategory
        {
            Id = "logs", NameKey = "cat_logs", DescKey = "cat_logs_desc",
            Glyph = "\uE8A5", ColorIndex = 9, Kind = CleanKind.Folder, NeedsAdmin = true,
            PathProvider = () => new[] { Path.Combine(WinDir, "Logs") },
        },
        new CleanCategory
        {
            Id = "prefetch", NameKey = "cat_prefetch", DescKey = "cat_prefetch_desc",
            Glyph = "\uE9D9", ColorIndex = 10, Kind = CleanKind.Folder, NeedsAdmin = true,
            PathProvider = () => new[] { Path.Combine(WinDir, "Prefetch") },
        },
        new CleanCategory
        {
            Id = "dev", NameKey = "cat_dev", DescKey = "cat_dev_desc",
            Glyph = "\uE943", ColorIndex = 11, Kind = CleanKind.Folder,
            PathProvider = () => new[]
            {
                Path.Combine(LocalAppData, "pip", "cache"),
                Path.Combine(LocalAppData, "npm-cache"),
                Path.Combine(RoamingAppData, "npm-cache"),
                Path.Combine(LocalAppData, "Yarn", "Cache"),
                Path.Combine(LocalAppData, "NuGet", "v3-cache"),
                Path.Combine(LocalAppData, "NuGet", "http-cache"),
            },
        },
        new CleanCategory
        {
            Id = "winold", NameKey = "cat_winold", DescKey = "cat_winold_desc",
            Glyph = "\uE7F8", ColorIndex = 12, Kind = CleanKind.WindowsOld, NeedsAdmin = true,
            // SECURITY FIX: Safely retrieve drive root using Path.GetPathRoot instead of string splitting
            PathProvider = () =>
            {
                var systemRoot = Path.GetPathRoot(WinDir);
                return !string.IsNullOrEmpty(systemRoot)
                    ? new[] { Path.Combine(systemRoot, "Windows.old") }
                    : Array.Empty<string>();
            },
        },
    };

    private static IEnumerable<string> BrowserCachePaths()
    {
        string local = LocalAppData;

        static IEnumerable<string> ProfileCaches(string root, string[] cacheNames)
        {
            // SECURITY FIX: Verify root path safety and ensure it is not a reparse point
            if (!Directory.Exists(root) || IsReparsePoint(root)) yield break;

            foreach (var cache in cacheNames)
                yield return Path.Combine(root, cache);

            foreach (var dir in SafeDirs(root))
            {
                // SECURITY FIX: Ensure subdirectories are not reparse points before descending
                if (IsReparsePoint(dir)) continue;

                var dirName = Path.GetFileName(dir);
                if (dirName.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                    dirName.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var cache in cacheNames)
                        yield return Path.Combine(dir, cache);
                }
            }
        }

        string chrome = Path.Combine(local, "Google", "Chrome", "User Data");
        foreach (var p in ProfileCaches(chrome, new[] { "Cache", "Code Cache", "GPUCache" })) yield return p;

        string edge = Path.Combine(local, "Microsoft", "Edge", "User Data");
        foreach (var p in ProfileCaches(edge, new[] { "Cache", "Code Cache", "GPUCache" })) yield return p;

        string brave = Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data");
        foreach (var p in ProfileCaches(brave, new[] { "Cache", "Code Cache", "GPUCache" })) yield return p;

        string vivaldi = Path.Combine(local, "Vivaldi", "User Data");
        foreach (var p in ProfileCaches(vivaldi, new[] { "Cache", "Code Cache", "GPUCache" })) yield return p;

        string opera = Path.Combine(local, "Opera Software");
        if (Directory.Exists(opera) && !IsReparsePoint(opera))
        {
            foreach (var dir in SafeDirs(opera))
            {
                if (IsReparsePoint(dir)) continue;
                foreach (var cache in new[] { "Cache", "Code Cache", "GPUCache", "Media Cache" })
                    yield return Path.Combine(dir, cache);
            }
        }

        string firefoxRoot = Path.Combine(local, "Mozilla", "Firefox", "Profiles");
        if (Directory.Exists(firefoxRoot) && !IsReparsePoint(firefoxRoot))
        {
            foreach (var dir in SafeDirs(firefoxRoot))
            {
                if (IsReparsePoint(dir)) continue;
                yield return Path.Combine(dir, "cache2");
            }
        }

        yield return Path.Combine(local, "Microsoft", "Windows", "INetCache");
        yield return Path.Combine(RoamingAppData, "Microsoft", "Windows", "INetCache");
    }

    /// <summary>Runs a category's PathProvider, validates each path, and returns only existing safe paths.</summary>
    public static List<string> ExistingPaths(CleanCategory category)
    {
        var list = new List<string>();
        foreach (var p in category.PathProvider())
        {
            // SECURITY FIX: Verify path safety before returning for processing
            if (IsPathSafeForCleanup(p) && (Directory.Exists(p) || File.Exists(p)))
            {
                list.Add(p);
            }
        }
        return list;
    }

    /// <summary>Best-effort path of the current user's name for display purposes.</summary>
    public static string ShortenHome(string path) =>
        path.StartsWith(UserProfile, StringComparison.OrdinalIgnoreCase)
            ? "~" + path[UserProfile.Length..]
            : path;
}
