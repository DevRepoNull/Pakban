using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Pakban.Localization;
using Pakban.Models;
using Pakban.Services;
using Serilog;

namespace Pakban.ViewModels;

public enum ScanPhase { Idle, Scanning, Done }
public enum CleanerTab { Standard, Orphans }

/// <summary>Row wrapper for one cleanable category.</summary>
public class CategoryVM : ViewModelBase
{
    public required CleanCategory Model { get; init; }

    public string NameKey => Model.NameKey;
    public string DescKey => Model.DescKey;
    public string Glyph => Model.Glyph;
    public Brush Brush => Palette.For(Model.ColorIndex);
    public bool Locked => Model.NeedsAdmin && !FileOps.IsElevated;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public long Bytes => Model.Bytes;
    public double BarFraction { get; private set; }

    public string SizeText => Model.Scanned ? FileOps.FormatSize(Model.Bytes) : "—";
    public string FilesText => Model.Scanned && Model.FileCount > 0
        ? $"{Model.FileCount:N0} {Loc.T("files")}"
        : Model.Scanned && Model.Kind == CleanKind.RecycleBin ? "" : "";

    public void RefreshSize(double maxBytes)
    {
        BarFraction = maxBytes > 0 ? Math.Min(1.0, Model.Bytes / maxBytes) : 0;
        Raise(nameof(SizeText));
        Raise(nameof(FilesText));
        Raise(nameof(Bytes));
        Raise(nameof(BarFraction));
    }

    public void RefreshLocalized()
    {
        Raise(nameof(SizeText));
        Raise(nameof(FilesText));
    }
}

/// <summary>Row wrapper for one orphaned data folder.</summary>
public class OrphanVM : ViewModelBase
{
    public required OrphanItem Model { get; init; }

    public long Bytes => Model.Bytes;
    public string Name => Path.GetFileName(Model.Path.TrimEnd(Path.DirectorySeparatorChar));
    public string SizeText => FileOps.FormatSize(Model.Bytes);
    public string ShortPath => FileOps.ShortenHome(Model.Path);

    private bool _isSelected = false; // Optional by default: leftover app data may contain user files
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public void RefreshLocalized()
    {
        Raise(nameof(SizeText));
        Raise(nameof(ShortPath));
        Raise(nameof(Name));
    }
}

public class CleanerViewModel : ViewModelBase
{
    public ObservableCollection<CategoryVM> Categories { get; private set; } = new();
    public ObservableCollection<OrphanVM> Orphans { get; } = new();

    private ScanPhase _phase = ScanPhase.Idle;
    public ScanPhase Phase { get => _phase; private set { if (Set(ref _phase, value)) Raise(nameof(IsBusy)); } }

    private bool _cleaning;
    public bool Cleaning { get => _cleaning; private set { if (Set(ref _cleaning, value)) Raise(nameof(IsBusy)); } }

    public bool IsBusy => _phase == ScanPhase.Scanning || _cleaning;

    private string _statusText = "";
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    private double _progress;
    public double Progress { get => _progress; private set => Set(ref _progress, value); }

    private CleanerTab _tab = CleanerTab.Standard;
    public CleanerTab Tab
    {
        get => _tab;
        set
        {
            if (Set(ref _tab, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private DateTime? _lastScan;
    public DateTime? LastScan { get => _lastScan; private set => Set(ref _lastScan, value); }

    public long TotalCleanable => Categories.Sum(c => c.Bytes);
    public long OrphanTotal => Orphans.Sum(o => o.Model.Bytes);
    public bool HasOrphans => Orphans.Count > 0;

    public event Action? ScanCompleted;

    private CancellationTokenSource? _cts;

    public RelayCommand ScanCommand { get; }
    public RelayCommand CancelScanCommand { get; }
    public RelayCommand CleanCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }

    public CleanerViewModel()
    {
        RebuildCategories(initial: true);
        Loc.I.LanguageChanged += () => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            foreach (var c in Categories) c.RefreshLocalized();
            foreach (var o in Orphans) o.RefreshLocalized();
        });

        ScanCommand = new RelayCommand(() => _ = ScanAsync(), () => !IsBusy);
        CancelScanCommand = new RelayCommand(() => _cts?.Cancel(), () => _phase == ScanPhase.Scanning);
        CleanCommand = new RelayCommand(() => _ = CleanAsync(), () => !IsBusy && HasSelection);
        SelectAllCommand = new RelayCommand(
            () => SetAllSelection(true),
            () => _phase == ScanPhase.Done && (Tab == CleanerTab.Standard ? Categories.Any(c => c.Model.Scanned) : Orphans.Count > 0));
        ClearSelectionCommand = new RelayCommand(
            () => SetAllSelection(false),
            () => Tab == CleanerTab.Standard ? Categories.Any(c => c.IsSelected) : Orphans.Any(o => o.IsSelected));
    }

    private bool HasSelection =>
        Categories.Any(c => c.IsSelected && !c.Locked && c.Model.Scanned && c.Model.Bytes > 0) ||
        Orphans.Any(o => o.IsSelected);

    private void SetAllSelection(bool selected)
    {
        if (Tab == CleanerTab.Orphans)
        {
            foreach (var o in Orphans)
                o.IsSelected = selected;
        }
        else
        {
            foreach (var c in Categories)
                if (!c.Locked && c.Model.Scanned)
                    c.IsSelected = selected;
        }
        CommandManager.InvalidateRequerySuggested();
    }

    private void RebuildCategories(bool initial)
    {
        var previous = Categories.ToDictionary(c => c.Model.Id, c => c.IsSelected);
        var models = FileOps.BuildCategories();
        Categories = new ObservableCollection<CategoryVM>(models.Select(m => new CategoryVM { Model = m }));
        foreach (var vm in Categories)
            if (previous.TryGetValue(vm.Model.Id, out var sel))
                vm.IsSelected = sel;
        Raise(nameof(Categories));
        if (!initial) RefreshAllSizes();
    }

    private void RefreshAllSizes()
    {
        double max = Categories.Where(c => c.Model.Scanned).Select(c => (double)c.Bytes).DefaultIfEmpty(0).Max();
        foreach (var c in Categories) c.RefreshSize(max);
        Raise(nameof(TotalCleanable));
        Raise(nameof(OrphanTotal));
        CommandManager.InvalidateRequerySuggested();
    }

    // ------------------------------------------------------------------ //
    //  Scanning                                                          //
    // ------------------------------------------------------------------ //

    public async Task ScanAsync()
    {
        if (_phase == ScanPhase.Scanning || _cleaning) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        Phase = ScanPhase.Scanning;
        Progress = 0;
        Orphans.Clear();
        Raise(nameof(HasOrphans));

        foreach (var vm in Categories)
        {
            vm.Model.Scanned = false;
            vm.Model.Bytes = 0;
            vm.Model.FileCount = 0;
            vm.IsSelected = false;
        }
        RefreshAllSizes();

        try
        {
            int done = 0;
            foreach (var vm in Categories)
            {
                ct.ThrowIfCancellationRequested();
                StatusText = Loc.F("status_scanning", Loc.T(vm.NameKey));
                var model = vm.Model;
                await Task.Run(() =>
                {
                    if (model.Kind == CleanKind.RecycleBin)
                    {
                        model.Bytes = FileOps.GetRecycleBinSize();
                        model.FileCount = 0;
                    }
                    else
                    {
                        long bytes = 0, files = 0;
                        // SECURITY FIX: ExistingPaths validates path safety and ensures no traversal outside base
                        foreach (var path in FileOps.ExistingPaths(model))
                        {
                            ct.ThrowIfCancellationRequested();
                            var (b, f) = model.Kind == CleanKind.Files
                                ? FileOps.MeasureFiles(path, model.FilePattern ?? "*", ct)
                                : FileOps.MeasureFolder(path, ct);
                            bytes += b;
                            files += f;
                        }
                        model.Bytes = bytes;
                        model.FileCount = files;
                    }
                    model.Scanned = true;
                }, ct).ConfigureAwait(true);

                done++;
                Progress = 100.0 * done / Categories.Count;
                RefreshAllSizes();
            }

            // Leftover (orphaned) data — the slow, deep pass.
            var progress = new Progress<(string Folder, int Done, int Total)>(p =>
            {
                StatusText = Loc.F("status_scanning", p.Folder) + $"  ({p.Done}/{p.Total})";
            });
            StatusText = Loc.F("status_scanning", Loc.T("tab_orphans"));
            var items = await OrphanDetection.FindAsync(ct, progress).ConfigureAwait(true);
            foreach (var item in items)
                Orphans.Add(new OrphanVM { Model = item });
            Raise(nameof(HasOrphans));
            Raise(nameof(OrphanTotal));

            Phase = ScanPhase.Done;
            LastScan = DateTime.Now;
            StatusText = Loc.T("status_ready");
        }
        catch (OperationCanceledException)
        {
            Phase = ScanPhase.Done;
            LastScan = DateTime.Now;
            StatusText = Loc.T("scan_cancelled");
        }
        finally
        {
            Progress = 100;
            RefreshAllSizes();
            ScanCompleted?.Invoke();
        }
    }

    // ------------------------------------------------------------------ //
    //  Cleaning                                                          //
    // ------------------------------------------------------------------ //

    public async Task CleanAsync()
    {
        var selected = Categories
            .Where(c => c.IsSelected && c.Model.Scanned && c.Model.Bytes > 0 && !c.Locked)
            .ToList();
        var selectedOrphans = Orphans.Where(o => o.IsSelected).ToList();
        if (selected.Count == 0 && selectedOrphans.Count == 0) return;

        long total = selected.Sum(c => c.Bytes) + selectedOrphans.Sum(o => o.Bytes);

        if (SettingsService.Current.ConfirmBeforeClean)
        {
            bool ok = await UiDialogs.ConfirmAsync(
                Loc.T("confirm_clean_title"),
                Loc.F("confirm_clean_msg", FileOps.FormatSize(total), selected.Count),
                danger: true).ConfigureAwait(true);
            if (!ok) return;
        }

        Cleaning = true;
        long freed = 0;
        long files = 0;
        int errors = 0;

        try
        {
            foreach (var vm in selected)
            {
                StatusText = Loc.F("status_cleaning", Loc.T(vm.NameKey));
                var model = vm.Model;
                var report = await Task.Run(() => ExecuteClean(model), CancellationToken.None).ConfigureAwait(true);
                freed += report.bytes;
                files += report.files;
                errors += report.errors;

                if (model.Kind is CleanKind.Folder or CleanKind.Files)
                {
                    // Re-measure what is left so the UI stays truthful.
                    var (b, f) = (0L, 0L);
                    foreach (var path in FileOps.ExistingPaths(model))
                    {
                        var r = model.Kind == CleanKind.Files
                            ? FileOps.MeasureFiles(path, model.FilePattern ?? "*", CancellationToken.None)
                            : FileOps.MeasureFolder(path, CancellationToken.None);
                        b += r.Bytes;
                        f += r.Files;
                    }
                    model.Bytes = b;
                    model.FileCount = f;
                }
                else
                {
                    model.Bytes = 0;
                    model.FileCount = 0;
                }
                vm.IsSelected = false;
                RefreshAllSizes();
            }

            if (selectedOrphans.Count > 0)
            {
                StatusText = Loc.F("status_cleaning", Loc.T("tab_orphans"));
                // SECURITY FIX: Filter and validate all orphan paths before calling shell Recycle Bin
                var validOrphanPaths = selectedOrphans
                    .Select(o => o.Model.Path)
                    .Where(FileOps.IsPathSafeForCleanup)
                    .ToList();

                if (validOrphanPaths.Count > 0)
                {
                    bool ok = await Task.Run(() => FileOps.MoveToRecycleBin(validOrphanPaths), CancellationToken.None).ConfigureAwait(true);
                    if (ok)
                    {
                        freed += selectedOrphans.Where(o => validOrphanPaths.Contains(o.Model.Path)).Sum(o => o.Bytes);
                        var removedList = selectedOrphans.Where(o => validOrphanPaths.Contains(o.Model.Path)).ToList();
                        foreach (var vm in removedList) Orphans.Remove(vm);
                        Raise(nameof(HasOrphans));
                        Raise(nameof(OrphanTotal));
                    }
                    else
                    {
                        errors += validOrphanPaths.Count;
                    }
                }
                else
                {
                    Log.Warning("SECURITY BLOCKED: All selected orphan paths failed security validation.");
                }
            }
        }
        finally
        {
            Cleaning = false;
            StatusText = Loc.T("status_ready");
            RefreshAllSizes();
            ScanCompleted?.Invoke();
        }

        string message = Loc.F("clean_done_msg", FileOps.FormatSize(freed), selected.Count, files);
        if (errors > 0) message += "\n\n" + Loc.T("clean_errors");
        await UiDialogs.ConfirmAsync(Loc.T("clean_done_title"), message, danger: false).ConfigureAwait(true);
    }

    private static (long bytes, long files, int errors) ExecuteClean(CleanCategory model)
    {
        Log.Information("Initiating cleanup for category: {CategoryId} (Kind: {Kind})", model.Id, model.Kind);

        switch (model.Kind)
        {
            case CleanKind.RecycleBin:
            {
                long size = FileOps.GetRecycleBinSize();
                FileOps.EmptyRecycleBin();
                return (size, 0, 0);
            }
            case CleanKind.WindowsOld:
            {
                var paths = FileOps.ExistingPaths(model);
                long size = 0;
                var safePaths = new List<string>();

                // SECURITY FIX: Strictly verify that target path is an actual Windows.old folder on a fixed drive
                foreach (var p in paths)
                {
                    if (FileOps.IsPathSafeForCleanup(p) &&
                        string.Equals(Path.GetFileName(p.TrimEnd('\\')), "Windows.old", StringComparison.OrdinalIgnoreCase))
                    {
                        safePaths.Add(p);
                        size += FileOps.MeasureFolder(p, CancellationToken.None).Bytes;
                    }
                    else
                    {
                        Log.Warning("SECURITY BLOCKED: Suspicious Windows.old path rejected: {Path}", p);
                    }
                }

                if (safePaths.Count == 0) return (0, 0, 0);

                bool ok = FileOps.MoveToRecycleBin(safePaths);
                return ok ? (size, 0, 0) : (0, 0, safePaths.Count);
            }
            case CleanKind.Files:
            {
                // SECURITY FIX: Replaced ad-hoc deletion with hardened DeleteFilesMatchingPattern.
                // Ensures symlinks are not traversed, ReadOnly is handled safely, and path traversal is blocked.
                long bytes = 0, files = 0; int errors = 0;
                foreach (var path in FileOps.ExistingPaths(model))
                {
                    var report = FileOps.DeleteFilesMatchingPattern(path, model.FilePattern ?? "*", CancellationToken.None);
                    bytes += report.Bytes;
                    files += report.Files;
                    errors += report.Errors;
                }
                return (bytes, files, errors);
            }
            default:
            {
                // SECURITY FIX: DeleteFolderContents enforces safe unlinking of junctions without recursion
                // and non-recursive directory deletion to prevent deleting target files.
                long bytes = 0, files = 0; int errors = 0;
                foreach (var path in FileOps.ExistingPaths(model))
                {
                    var r = FileOps.DeleteFolderContents(path, CancellationToken.None);
                    bytes += r.Bytes;
                    files += r.Files;
                    errors += r.Errors;
                }
                return (bytes, files, errors);
            }
        }
    }
}
