using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Pakban.Models;
using Pakban.Services;

namespace Pakban.ViewModels;

/// <summary>
/// UI-bindable representation of a system drive with selection support.
/// </summary>
public class DriveItemViewModel : ViewModelBase
{
    private readonly Action? _onSelectionChanged;
    private bool _isSelected = true;

    public DriveInfo Drive { get; }
    public string Name => Drive.Name; // e.g. "C:\"
    public string DriveLetter => Drive.Name.TrimEnd('\\');
    public string VolumeLabel { get; }
    public string DisplayName { get; }
    public DriveType DriveType => Drive.DriveType;
    public long TotalSize { get; }
    public long FreeSpace { get; }
    public long UsedSpace => Math.Max(0, TotalSize - FreeSpace);
    public double UsedPercentage => TotalSize > 0 ? (double)UsedSpace / TotalSize * 100.0 : 0.0;

    public string TotalSizeFormatted => FileOps.FormatSize(TotalSize);
    public string FreeSpaceFormatted => FileOps.FormatSize(FreeSpace);
    public string UsedSpaceFormatted => FileOps.FormatSize(UsedSpace);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value))
            {
                _onSelectionChanged?.Invoke();
            }
        }
    }

    public DriveItemViewModel(DriveInfo drive, Action? onSelectionChanged = null)
    {
        Drive = drive ?? throw new ArgumentNullException(nameof(drive));
        _onSelectionChanged = onSelectionChanged;

        string label = string.Empty;
        long total = 0;
        long free = 0;

        try
        {
            if (drive.IsReady)
            {
                label = drive.VolumeLabel;
                total = drive.TotalSize;
                free = drive.AvailableFreeSpace;
            }
        }
        catch
        {
            // Fallback for drives that do not report label/size
        }

        VolumeLabel = label;
        TotalSize = total;
        FreeSpace = free;

        DisplayName = string.IsNullOrWhiteSpace(label)
            ? $"{DriveLetter} ({drive.DriveType})"
            : $"{label} ({DriveLetter})";
    }

    internal void SetSelectionSilent(bool isSelected)
    {
        Set(ref _isSelected, isSelected, nameof(IsSelected));
    }
}

/// <summary>
/// UI-bindable item representing a detected standard junk category.
/// </summary>
public class JunkItemViewModel : ViewModelBase
{
    private bool _isSelected = true;

    public required ScannedJunkItem Model { get; init; }

    public string Id => Model.Id;
    public string CategoryName => Model.CategoryName;
    public string DriveLetter => Model.DriveLetter;
    public string TargetPath => Model.TargetPath;
    public long TotalBytes => Model.TotalBytes;
    public long FileCount => Model.FileCount;
    public string FormattedSize => FileOps.FormatSize(Model.TotalBytes);

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }
}

/// <summary>
/// UI-bindable item representing an orphaned or leftover application folder.
/// IMPORTANT: Leftover data may contain personal configuration or recovered user files.
/// Hence, it is OPTIONAL and UNCHECKED (IsSelected = false) by default.
/// </summary>
public class LeftoverFolderViewModel : ViewModelBase
{
    private readonly Action? _onSelectionChanged;
    private bool _isSelected = false; // Optional by default for safety

    public OrphanItem Model { get; init; }

    public string Name => Path.GetFileName(Model.Path.TrimEnd(Path.DirectorySeparatorChar));
    public string FullPath => Model.Path;
    public string ShortPath => FileOps.ShortenHome(Model.Path);
    public long Bytes => Model.Bytes;
    public string FormattedSize => FileOps.FormatSize(Model.Bytes);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value))
            {
                _onSelectionChanged?.Invoke();
            }
        }
    }

    public LeftoverFolderViewModel(OrphanItem model, Action? onSelectionChanged = null)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        _onSelectionChanged = onSelectionChanged;
    }

    internal void SetSelectionSilent(bool isSelected)
    {
        Set(ref _isSelected, isSelected, nameof(IsSelected));
    }
}

/// <summary>
/// ViewModel coordinating drive selection, background storage scanning, progress updates,
/// junk results, and optional leftover data folder management according to the MVVM pattern.
/// </summary>
public class StorageScannerViewModel : ViewModelBase
{
    private readonly IStorageScanner _scanner;
    private CancellationTokenSource? _cts;

    private bool _isScanning;
    private bool? _isAllDrivesSelected = true;
    private bool? _isAllLeftoversSelected = false;
    private double _scanProgressPercentage;
    private string _currentStatusText = "Ready to scan";
    private long _totalJunkBytes;
    private long _totalJunkFiles;
    private TimeSpan _scanDuration;
    private bool _isSyncingDriveSelection;
    private bool _isSyncingLeftoversSelection;

    // Collections
    public ObservableCollection<DriveItemViewModel> Drives { get; } = new();
    public ObservableCollection<JunkItemViewModel> JunkItems { get; } = new();
    public ObservableCollection<LeftoverFolderViewModel> LeftoverFolders { get; } = new();

    // Bindable Properties
    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (Set(ref _isScanning, value))
            {
                Raise(nameof(CanScan));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool CanScan => !IsScanning && Drives.Any(d => d.IsSelected);

    public bool? IsAllDrivesSelected
    {
        get => _isAllDrivesSelected;
        set
        {
            if (Set(ref _isAllDrivesSelected, value) && !_isSyncingDriveSelection && value.HasValue)
            {
                SelectAllDrives(value.Value);
            }
        }
    }

    /// <summary>
    /// Tri-state selection property for leftover/orphaned data folders.
    /// Allows selecting all, clearing all, or displaying indeterminate state.
    /// </summary>
    public bool? IsAllLeftoversSelected
    {
        get => _isAllLeftoversSelected;
        set
        {
            if (Set(ref _isAllLeftoversSelected, value) && !_isSyncingLeftoversSelection && value.HasValue)
            {
                SelectAllLeftovers(value.Value);
            }
        }
    }

    public bool HasLeftovers => LeftoverFolders.Count > 0;

    public long TotalLeftoverBytes => LeftoverFolders.Sum(f => f.Bytes);
    public string FormattedTotalLeftoverBytes => FileOps.FormatSize(TotalLeftoverBytes);

    public double ScanProgressPercentage
    {
        get => _scanProgressPercentage;
        private set => Set(ref _scanProgressPercentage, value);
    }

    public string CurrentStatusText
    {
        get => _currentStatusText;
        private set => Set(ref _currentStatusText, value);
    }

    public long TotalJunkBytes
    {
        get => _totalJunkBytes;
        private set
        {
            if (Set(ref _totalJunkBytes, value))
            {
                Raise(nameof(FormattedTotalJunkSize));
            }
        }
    }

    public string FormattedTotalJunkSize => FileOps.FormatSize(TotalJunkBytes);

    public long TotalJunkFiles
    {
        get => _totalJunkFiles;
        private set => Set(ref _totalJunkFiles, value);
    }

    public TimeSpan ScanDuration
    {
        get => _scanDuration;
        private set => Set(ref _scanDuration, value);
    }

    // Commands
    public ICommand ScanCommand { get; }
    public ICommand CancelScanCommand { get; }
    public ICommand RefreshDrivesCommand { get; }
    public ICommand SelectAllDrivesCommand { get; }
    public ICommand DeselectAllDrivesCommand { get; }

    // Leftover Data Specific Commands
    public ICommand SelectAllLeftoversCommand { get; }
    public ICommand ClearLeftoversSelectionCommand { get; }

    public StorageScannerViewModel() : this(new StorageScannerService())
    {
    }

    public StorageScannerViewModel(IStorageScanner scanner)
    {
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));

        ScanCommand = new RelayCommand(async () => await StartScanAsync(), () => CanScan);
        CancelScanCommand = new RelayCommand(CancelScan, () => IsScanning);
        RefreshDrivesCommand = new RelayCommand(LoadDrives, () => !IsScanning);
        SelectAllDrivesCommand = new RelayCommand(() => SelectAllDrives(true), () => !IsScanning);
        DeselectAllDrivesCommand = new RelayCommand(() => SelectAllDrives(false), () => !IsScanning);

        SelectAllLeftoversCommand = new RelayCommand(() => SelectAllLeftovers(true), () => HasLeftovers && !IsScanning);
        ClearLeftoversSelectionCommand = new RelayCommand(() => SelectAllLeftovers(false), () => HasLeftovers && !IsScanning);

        LoadDrives();
    }

    public void LoadDrives()
    {
        Drives.Clear();
        var systemDrives = _scanner.GetAvailableDrives();

        foreach (var drive in systemDrives)
        {
            Drives.Add(new DriveItemViewModel(drive, OnDriveSelectionChanged));
        }

        UpdateAllDrivesSelectedState();
        Raise(nameof(CanScan));
    }

    private void OnDriveSelectionChanged()
    {
        UpdateAllDrivesSelectedState();
        Raise(nameof(CanScan));
        CommandManager.InvalidateRequerySuggested();
    }

    private void SelectAllDrives(bool select)
    {
        _isSyncingDriveSelection = true;
        try
        {
            foreach (var drive in Drives)
            {
                drive.SetSelectionSilent(select);
            }
        }
        finally
        {
            _isSyncingDriveSelection = false;
        }

        UpdateAllDrivesSelectedState();
        Raise(nameof(CanScan));
        CommandManager.InvalidateRequerySuggested();
    }

    private void UpdateAllDrivesSelectedState()
    {
        if (Drives.Count == 0)
        {
            _isAllDrivesSelected = false;
        }
        else if (Drives.All(d => d.IsSelected))
        {
            _isAllDrivesSelected = true;
        }
        else if (Drives.All(d => !d.IsSelected))
        {
            _isAllDrivesSelected = false;
        }
        else
        {
            _isAllDrivesSelected = null;
        }

        Raise(nameof(IsAllDrivesSelected));
    }

    // ------------------------------------------------------------------ //
    //  Leftover Data Selection Methods                                   //
    // ------------------------------------------------------------------ //

    private void OnLeftoverSelectionChanged()
    {
        UpdateAllLeftoversSelectedState();
        CommandManager.InvalidateRequerySuggested();
    }

    public void SelectAllLeftovers(bool select)
    {
        _isSyncingLeftoversSelection = true;
        try
        {
            foreach (var folder in LeftoverFolders)
            {
                folder.SetSelectionSilent(select);
            }
        }
        finally
        {
            _isSyncingLeftoversSelection = false;
        }

        UpdateAllLeftoversSelectedState();
        CommandManager.InvalidateRequerySuggested();
    }

    private void UpdateAllLeftoversSelectedState()
    {
        if (LeftoverFolders.Count == 0)
        {
            _isAllLeftoversSelected = false;
        }
        else if (LeftoverFolders.All(f => f.IsSelected))
        {
            _isAllLeftoversSelected = true;
        }
        else if (LeftoverFolders.All(f => !f.IsSelected))
        {
            _isAllLeftoversSelected = false;
        }
        else
        {
            _isAllLeftoversSelected = null; // Indeterminate
        }

        Raise(nameof(IsAllLeftoversSelected));
    }

    // ------------------------------------------------------------------ //
    //  Scanning Operations                                               //
    // ------------------------------------------------------------------ //

    public async Task StartScanAsync()
    {
        if (IsScanning) return;

        var selectedDrives = Drives.Where(d => d.IsSelected).Select(d => d.Drive).ToList();
        if (selectedDrives.Count == 0) return;

        IsScanning = true;
        ScanProgressPercentage = 0;
        CurrentStatusText = "Initializing storage scan...";
        JunkItems.Clear();
        LeftoverFolders.Clear();
        TotalJunkBytes = 0;
        TotalJunkFiles = 0;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        var progressHandler = new Progress<StorageScanProgress>(report =>
        {
            ScanProgressPercentage = report.ProgressPercentage;
            CurrentStatusText = $"{report.CurrentCategory}: {report.CurrentPath}";
            TotalJunkBytes = report.ScannedBytes;
            TotalJunkFiles = report.ScannedFiles;
        });

        try
        {
            var report = await _scanner.ScanDrivesAsync(selectedDrives, progressHandler, token);

            // 1. Add standard junk categories
            foreach (var item in report.Items)
            {
                JunkItems.Add(new JunkItemViewModel { Model = item });
            }

            // 2. Add discovered leftover/orphaned folders (all unchecked by default for safety!)
            foreach (var orphan in report.LeftoverFolders)
            {
                LeftoverFolders.Add(new LeftoverFolderViewModel(orphan, OnLeftoverSelectionChanged));
            }

            TotalJunkBytes = report.TotalBytes;
            TotalJunkFiles = report.TotalFiles;
            ScanDuration = report.Duration;
            UpdateAllLeftoversSelectedState();

            CurrentStatusText = $"Scan finished in {report.Duration.TotalSeconds:F1}s — Found {FormattedTotalJunkSize} ({report.TotalFiles:N0} files, {LeftoverFolders.Count} leftover folders)";
        }
        catch (OperationCanceledException)
        {
            CurrentStatusText = "Scan cancelled by user.";
        }
        catch (Exception ex)
        {
            CurrentStatusText = $"Error during scan: {ex.Message}";
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            IsScanning = false;
            ScanProgressPercentage = 100;
            Raise(nameof(HasLeftovers));
            Raise(nameof(TotalLeftoverBytes));
            Raise(nameof(FormattedTotalLeftoverBytes));
        }
    }

    public void CancelScan()
    {
        _cts?.Cancel();
    }
}
