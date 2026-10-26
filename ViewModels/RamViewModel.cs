using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Pakban.Controls;
using Pakban.Localization;
using Pakban.Services;

namespace Pakban.ViewModels;

public class ProcessVM : ViewModelBase
{
    public required string Name { get; init; }
    public required long WorkingSet { get; init; }
    public string SizeText => FileOps.FormatSize(WorkingSet);
    public Brush Brush { get; init; } = Palette.For(0);
    public double Fraction { get; init; }
}

public class RamViewModel : ViewModelBase
{
    private readonly DispatcherTimer _statsTimer;
    private readonly DispatcherTimer _processTimer;
    private readonly List<double> _history = new();

    private string _totalText = "";
    public string TotalText { get => _totalText; private set => Set(ref _totalText, value); }

    private string _usedText = "";
    public string UsedText { get => _usedText; private set => Set(ref _usedText, value); }

    private string _availableText = "";
    public string AvailableText { get => _availableText; private set => Set(ref _availableText, value); }

    private string _cachedText = "";
    public string CachedText { get => _cachedText; private set => Set(ref _cachedText, value); }

    private int _loadPercent;
    public int LoadPercent { get => _loadPercent; private set => Set(ref _loadPercent, value); }

    public string CenterText => $"{FileOps.Lrm}{LoadPercent}%{FileOps.Lrm}";
    public string CenterSubText => Loc.T("ram_used");

    private IReadOnlyList<DonutSegment>? _usageSegments;
    public IReadOnlyList<DonutSegment>? UsageSegments
    {
        get => _usageSegments;
        private set => Set(ref _usageSegments, value);
    }

    private IReadOnlyList<double> _points = Array.Empty<double>();
    public IReadOnlyList<double> Points { get => _points; private set { _points = value; Raise(nameof(Points)); } }

    public ObservableCollection<ProcessVM> TopProcesses { get; } = new();

    private bool _optimizing;
    public bool Optimizing { get => _optimizing; private set => Set(ref _optimizing, value); }

    public RelayCommand OptimizeCommand { get; }
    public RelayCommand RefreshProcessesCommand { get; }

    public RamViewModel()
    {
        for (int i = 0; i < 90; i++) _history.Add(0);
        UpdateStats();
        UpdateProcesses();

        _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statsTimer.Tick += (_, _) => UpdateStats();
        _statsTimer.Start();

        _processTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _processTimer.Tick += (_, _) => UpdateProcesses();
        _processTimer.Start();

        OptimizeCommand = new RelayCommand(() => _ = OptimizeAsync(), () => !Optimizing);
        RefreshProcessesCommand = new RelayCommand(UpdateProcesses);

        Loc.I.LanguageChanged += () => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            Raise(nameof(CenterSubText));
            foreach (var p in TopProcesses) p.Raise(nameof(p.SizeText));
        });
    }

    private void UpdateStats()
    {
        var stats = RamService.GetStats();
        TotalText = FileOps.FormatSize((long)(stats.TotalGB * (1L << 30)));
        UsedText = FileOps.FormatSize((long)(stats.UsedGB * (1L << 30)));
        AvailableText = FileOps.FormatSize((long)(stats.AvailableGB * (1L << 30)));
        CachedText = FileOps.FormatSize((long)(stats.CachedGB * (1L << 30)));
        LoadPercent = stats.LoadPercent;
        Raise(nameof(CenterText));

        var accent = Application.Current.Resources["AccentBrush"] as Brush ?? Palette.For(0);
        UsageSegments = new List<DonutSegment>
        {
            new() { Value = Math.Max(1, stats.LoadPercent), Brush = accent, Label = Loc.T("ram_used"), Detail = UsedText },
            new() { Value = Math.Max(1, 100 - stats.LoadPercent), Brush = Palette.For(11), Label = Loc.T("ram_available"), Detail = AvailableText },
        };

        _history.RemoveAt(0);
        _history.Add(stats.LoadPercent);
        Points = _history.ToArray();
    }

    private void UpdateProcesses()
    {
        var top = RamService.GetTopProcesses();
        long max = top.Count > 0 ? top[0].WorkingSet : 1;
        TopProcesses.Clear();
        for (int i = 0; i < top.Count; i++)
        {
            var p = top[i];
            TopProcesses.Add(new ProcessVM
            {
                Name = p.Name,
                WorkingSet = p.WorkingSet,
                Fraction = Math.Min(1.0, (double)p.WorkingSet / max),
                Brush = Palette.For(i % 7),
            });
        }
    }

    private async Task OptimizeAsync()
    {
        Optimizing = true;
        try
        {
            var result = await Task.Run(RamService.Optimize).ConfigureAwait(true);
            UpdateStats();
            UpdateProcesses();
            await UiDialogs.ConfirmAsync(
                Loc.T("ram_optimized_title"),
                Loc.F("ram_optimized_msg", FileOps.FormatSize((long)(result.FreedGB * (1L << 30)))),
                danger: false).ConfigureAwait(true);
        }
        finally
        {
            Optimizing = false;
        }
    }
}
