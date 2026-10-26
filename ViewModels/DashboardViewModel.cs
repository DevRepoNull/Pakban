using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Pakban.Controls;
using Pakban.Localization;
using Pakban.Services;

namespace Pakban.ViewModels;

public class DriveVM : ViewModelBase
{
    public required string Label { get; init; }
    public string UsedText { get; private set; } = "";
    public string FreeText { get; private set; } = "";
    public double UsedFraction { get; private set; }

    public static DriveVM? From(DriveInfo drive)
    {
        try
        {
            if (drive.DriveType != DriveType.Fixed || !drive.IsReady) return null;
            double total = drive.TotalSize;
            double free = drive.AvailableFreeSpace;
            if (total <= 0) return null;
            var name = drive.Name.TrimEnd('\\', '/');
            if (!name.EndsWith(':')) name += ":";
            var vm = new DriveVM { Label = $"{FileOps.Lrm}{name}{FileOps.Lrm}" };
            vm.UsedText = Loc.F("dash_used") + " " + FileOps.FormatSize((long)(total - free));
            vm.FreeText = FileOps.FormatSize((long)free) + " " + Loc.T("dash_free");
            vm.UsedFraction = (total - free) / total;
            return vm;
        }
        catch { return null; }
    }
}

public class LegendVM
{
    public required Brush Brush { get; init; }
    public required string Label { get; init; }
    public required string SizeText { get; init; }
    public required double Fraction { get; init; }
}

public class DashboardViewModel : ViewModelBase
{
    private readonly CleanerViewModel _cleaner;
    private readonly DispatcherTimer _ramTimer;

    public ObservableCollection<LegendVM> Legend { get; } = new();
    public ObservableCollection<DriveVM> Drives { get; } = new();

    private IReadOnlyList<DonutSegment>? _segments;
    public IReadOnlyList<DonutSegment>? Segments { get => _segments; private set => Set(ref _segments, value); }

    private string _cleanableText = "—";
    public string CleanableText { get => _cleanableText; private set => Set(ref _cleanableText, value); }

    private string _itemsText = "—";
    public string ItemsText { get => _itemsText; private set => Set(ref _itemsText, value); }

    private string _ramText = "—";
    public string RamText { get => _ramText; private set => Set(ref _ramText, value); }

    private string _largestText = "—";
    public string LargestText { get => _largestText; private set => Set(ref _largestText, value); }

    private string _lastScanText = "";
    public string LastScanText { get => _lastScanText; private set => Set(ref _lastScanText, value); }

    private bool _hasData;
    public bool HasData { get => _hasData; private set => Set(ref _hasData, value); }

    public string CenterText => CleanableText;
    public string CenterSubText => Loc.T("dash_total_cleanable");

    public Action? NavigateToCleaner { get; set; }
    public RelayCommand GoCleanCommand { get; }

    public DashboardViewModel(CleanerViewModel cleaner)
    {
        _cleaner = cleaner;
        cleaner.ScanCompleted += () => Application.Current?.Dispatcher.BeginInvoke(Rebuild);

        _ramTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _ramTimer.Tick += (_, _) => UpdateRam();
        _ramTimer.Start();
        UpdateRam();
        Rebuild();

        Loc.I.LanguageChanged += () => Application.Current?.Dispatcher.BeginInvoke(Rebuild);

        GoCleanCommand = new RelayCommand(() => NavigateToCleaner?.Invoke());
    }

    private void UpdateRam()
    {
        var stats = RamService.GetStats();
        RamText = $"{FileOps.Lrm}{stats.LoadPercent}%{FileOps.Lrm}";
    }

    private void Rebuild()
    {
        UpdateRam();
        RefreshDrives();

        long total = _cleaner.TotalCleanable + _cleaner.OrphanTotal;
        long items = _cleaner.Categories.Sum(c => c.Model.FileCount);
        CleanableText = _cleaner.Phase == ScanPhase.Done ? FileOps.FormatSize(total) : "—";
        ItemsText = _cleaner.Phase == ScanPhase.Done ? $"{items:N0}" : "—";

        var largest = _cleaner.Categories
            .Where(c => c.Model.Scanned && c.Bytes > 0)
            .OrderByDescending(c => c.Bytes)
            .FirstOrDefault();
        LargestText = _cleaner.Phase == ScanPhase.Done
            ? (largest is null ? "—" : Loc.T(largest.NameKey))
            : "—";

        if (_cleaner.LastScan is { } scan)
            LastScanText = Loc.T("dash_last_scan") + ": " + FileOps.Lrm + scan.ToString("yyyy/MM/dd  HH:mm") + FileOps.Lrm;
        else
            LastScanText = Loc.T("dash_never");

        HasData = _cleaner.Phase == ScanPhase.Done && total > 0;

        BuildDonut();
        Raise(nameof(CenterText));
        Raise(nameof(CenterSubText));
    }

    private void BuildDonut()
    {
        Legend.Clear();
        var segments = new List<DonutSegment>();

        var sized = _cleaner.Categories
            .Where(c => c.Model.Scanned && c.Bytes > 0)
            .OrderByDescending(c => c.Bytes)
            .ToList();

        long orphanBytes = _cleaner.OrphanTotal;
        long total = sized.Sum(c => c.Bytes) + orphanBytes;
        if (total <= 0)
        {
            Segments = segments;
            return;
        }

        var top = sized.Take(7).ToList();
        foreach (var vm in top)
        {
            var brush = vm.Brush;
            segments.Add(new DonutSegment
            {
                Value = vm.Bytes,
                Brush = brush,
                Label = Loc.T(vm.NameKey),
                Detail = FileOps.FormatSize(vm.Bytes),
            });
            Legend.Add(new LegendVM
            {
                Brush = brush,
                Label = Loc.T(vm.NameKey),
                SizeText = FileOps.FormatSize(vm.Bytes),
                Fraction = (double)vm.Bytes / total,
            });
        }

        long rest = sized.Skip(7).Sum(c => c.Bytes);
        if (rest > 0)
        {
            var brush = Palette.For(11);
            segments.Add(new DonutSegment
            {
                Value = rest,
                Brush = brush,
                Label = Loc.T("dash_other"),
                Detail = FileOps.FormatSize(rest),
            });
            Legend.Add(new LegendVM
            {
                Brush = brush,
                Label = Loc.T("dash_other"),
                SizeText = FileOps.FormatSize(rest),
                Fraction = (double)rest / total,
            });
        }

        if (orphanBytes > 0)
        {
            segments.Add(new DonutSegment
            {
                Value = orphanBytes,
                Brush = Palette.Danger,
                Label = Loc.T("dash_orphans"),
                Detail = FileOps.FormatSize(orphanBytes),
            });
            Legend.Add(new LegendVM
            {
                Brush = Palette.Danger,
                Label = Loc.T("dash_orphans"),
                SizeText = FileOps.FormatSize(orphanBytes),
                Fraction = (double)orphanBytes / total,
            });
        }

        Segments = segments;
    }

    private void RefreshDrives()
    {
        Drives.Clear();
        foreach (var drive in DriveInfo.GetDrives())
        {
            var vm = DriveVM.From(drive);
            if (vm is not null) Drives.Add(vm);
        }
    }
}
