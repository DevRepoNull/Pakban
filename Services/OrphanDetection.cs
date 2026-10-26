using System.IO;
using Pakban.Models;
using Serilog;

namespace Pakban.Services;

/// <summary>
/// Finds top-level data folders that most likely belong to apps which are no longer
/// installed. Heuristic by nature: results are always moved to the Recycle Bin, never
/// deleted permanently.
/// Hardened against Reparse Points / Junction traps and Path Traversal.
/// </summary>
public static class OrphanDetection
{
    // Folders that are part of Windows itself or containers we must not touch.
    private static readonly HashSet<string> Whitelist = new(StringComparer.OrdinalIgnoreCase)
    {
        "microsoft", "windows", "temp", "packages", "d3dscache", "squirreltemp",
        "comms", "connecteddevicesplatform", "programs", "pip", "npm-cache", "npm",
        "yarn", "nuget", "docker", "desktop", "low", "google", "mozilla", "opera software",
        "bravesoftware", "vivaldi", "nvidia corporation", "amd", "intel", "apple", "adobe",
        "powershell", "microsoft corporation", "windows error reporting", "crashdumps",
        "iconcache.db", "desktop.ini", "gamebar", "slack", "groups", "publisher cache",
        "wercplsupport", "ces", "ida", "regid.1991-06.com.microsoft",
        "package cache", "chocolatey", "scoop", "winget", "onedrive", "onedrivetemp",
        "usoshared", "usoprivate", "toastnotificationmanagercompat", "cache",
        "matevm", "softperfect", "kaspersky lab", "avg", "avast software", "mcafee",
    };

    // Vendor containers: judge their children individually instead of the root itself.
    private static readonly HashSet<string> VendorRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        "google", "mozilla", "bravesoftware", "vivaldi", "opera software",
        "nvidia corporation", "amd", "intel", "adobe", "apple",
    };

    private static string Normalize(string name)
    {
        var s = name.ToLowerInvariant();
        foreach (var suffix in new[] { " corporation", " software", " inc", " ltd", " llc", " labs" })
        {
            if (s.EndsWith(suffix, StringComparison.Ordinal))
                s = s[..^suffix.Length];
        }
        return s.Replace(" ", "").Replace("-", "").Replace("_", "").Replace(".", "");
    }

    /// <summary>Collects every name that can prove "an app with this identity is installed".</summary>
    private static HashSet<string> CollectInstalledIdentities()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            var n = Normalize(raw);
            if (n.Length >= 3) names.Add(n);
        }

        foreach (var root in new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        })
        {
            foreach (var hive in new[]
            {
                Microsoft.Win32.Registry.LocalMachine,
                Microsoft.Win32.Registry.CurrentUser,
            })
            {
                try
                {
                    using var key = hive.OpenSubKey(root);
                    if (key is null) continue;
                    foreach (var sub in key.GetSubKeyNames())
                    {
                        try
                        {
                            using var item = key.OpenSubKey(sub);
                            if (item is null) continue;
                            Add(item.GetValue("DisplayName") as string);
                            Add(item.GetValue("Publisher") as string);
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        // Start-menu shortcut names are a good secondary source.
        foreach (var dir in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
        })
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                // SECURITY FIX: Skip reparse points when scanning shortcuts
                foreach (var lnk in Directory.EnumerateFiles(dir, "*.lnk", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                }))
                {
                    Add(Path.GetFileNameWithoutExtension(lnk));
                }
            }
            catch { }
        }

        return names;
    }

    private static bool MatchesInstalled(string folderName, HashSet<string> installed)
    {
        var f = Normalize(folderName);
        if (f.Length < 3) return true; // too short to judge — treat as known, never flag
        foreach (var app in installed)
        {
            if (app.Contains(f, StringComparison.Ordinal) || f.Contains(app, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Scans the standard per-app data roots and returns folders whose owning app
    /// appears to be gone. Reports each finished root folder through <paramref name="progress"/>.
    /// Hardened against Reparse Points / Junctions.
    /// </summary>
    public static async Task<List<OrphanItem>> FindAsync(
        CancellationToken ct,
        IProgress<(string Folder, int Done, int Total)>? progress = null)
    {
        var installed = await Task.Run(CollectInstalledIdentities, ct).ConfigureAwait(false);
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        };

        var result = new List<OrphanItem>();
        var topLevel = new List<string>();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root) || FileOps.IsReparsePoint(root)) continue;
            try
            {
                foreach (var d in Directory.EnumerateDirectories(root))
                {
                    // SECURITY FIX: Skip any folder that is a Reparse Point (Junction/Symlink)
                    // to prevent treating malicious link traps as orphaned app folders (CWE-59).
                    if (!FileOps.IsReparsePoint(d) && FileOps.IsPathSafeForCleanup(d))
                    {
                        topLevel.Add(d);
                    }
                }
            }
            catch { }
        }

        int done = 0, total = topLevel.Count;
        foreach (var folder in topLevel)
        {
            ct.ThrowIfCancellationRequested();
            done++;
            var name = Path.GetFileName(folder);
            progress?.Report((name, done, total));

            if (Whitelist.Contains(name)) continue;

            var toJudge = new List<(string Path, string Name)> { (folder, name) };
            if (VendorRoots.Contains(name))
            {
                toJudge.Clear();
                try
                {
                    foreach (var child in Directory.EnumerateDirectories(folder))
                    {
                        // SECURITY FIX: Skip reparse points inside vendor directories
                        if (!FileOps.IsReparsePoint(child) && FileOps.IsPathSafeForCleanup(child))
                        {
                            toJudge.Add((child, Path.GetFileName(child)));
                        }
                    }
                }
                catch { }
            }

            foreach (var (path, itemName) in toJudge)
            {
                if (Whitelist.Contains(itemName)) continue;
                if (MatchesInstalled(itemName, installed)) continue;

                ct.ThrowIfCancellationRequested();

                // SECURITY FIX: Re-verify path safety and ensure folder is not a reparse point
                if (!FileOps.IsPathSafeForCleanup(path) || FileOps.IsReparsePoint(path))
                    continue;

                var (bytes, files) = await Task.Run(
                    () => FileOps.MeasureFolder(path, ct), ct).ConfigureAwait(false);
                if (bytes > 0)
                {
                    result.Add(new OrphanItem { Path = path, Bytes = bytes });
                }
            }
        }

        result.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
        return result;
    }
}
