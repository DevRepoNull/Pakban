namespace Pakban.Models;

public enum CleanKind
{
    /// <summary>Delete the folder's contents permanently.</summary>
    Folder,
    /// <summary>Delete only files matching a pattern (folder kept).</summary>
    Files,
    /// <summary>Empty the Recycle Bin through the shell API.</summary>
    RecycleBin,
    /// <summary>Orphaned app-data folders — always moved to the Recycle Bin.</summary>
    Orphans,
    /// <summary>Windows.old — moved to the Recycle Bin because it is recoverable data.</summary>
    WindowsOld,
}

/// <summary>Definition + live scan result of one cleanable category.</summary>
public class CleanCategory
{
    public required string Id { get; init; }
    public required string NameKey { get; init; }
    public required string DescKey { get; init; }
    public required string Glyph { get; init; }
    public int ColorIndex { get; init; }
    public bool NeedsAdmin { get; init; }
    public CleanKind Kind { get; init; }
    public string? FilePattern { get; init; }
    public Func<IEnumerable<string>> PathProvider { get; init; } = () => Array.Empty<string>();

    public long Bytes { get; set; }
    public long FileCount { get; set; }
    public bool Scanned { get; set; }
}

/// <summary>A top-level data folder whose owning app is not installed anymore.</summary>
public class OrphanItem
{
    public required string Path { get; init; }
    public long Bytes { get; set; }
}
