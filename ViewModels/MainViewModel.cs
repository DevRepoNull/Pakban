using System.Collections.ObjectModel;
using System.Windows;
using Pakban.Localization;
using Pakban.Services;
using Pakban.ViewModels;

namespace Pakban.ViewModels;

public class NavItemVM : ViewModelBase
{
    public required int Index { get; init; }
    public required string Glyph { get; init; }
    public required string Key { get; init; }
    public string Label => Loc.T(Key);
}

public class MainViewModel : ViewModelBase
{
    public CleanerViewModel Cleaner { get; }
    public DashboardViewModel Dashboard { get; }
    public RamViewModel Ram { get; }
    public SettingsViewModel Settings { get; }

    public ObservableCollection<NavItemVM> NavItems { get; } = new()
    {
        new NavItemVM { Index = 0, Glyph = "\uE80F", Key = "nav_dashboard" },
        new NavItemVM { Index = 1, Glyph = "\uE74D", Key = "nav_cleaner" },
        new NavItemVM { Index = 2, Glyph = "\uE9D9", Key = "nav_ram" },
        new NavItemVM { Index = 3, Glyph = "\uE713", Key = "nav_settings" },
    };

    private int _navIndex;
    public int NavIndex
    {
        get => _navIndex;
        set
        {
            if (Set(ref _navIndex, value))
            {
                CurrentPage = value switch
                {
                    1 => Cleaner,
                    2 => Ram,
                    3 => Settings,
                    _ => Dashboard,
                };
            }
        }
    }

    private object _currentPage = default!;
    public object CurrentPage { get => _currentPage; private set => Set(ref _currentPage, value); }

    public bool IsElevated => FileOps.IsElevated;

    public RelayCommand ElevateCommand { get; }
    public RelayCommand ToggleLanguageCommand { get; }
    public RelayCommand ToggleThemeCommand { get; }

    public string ThemeGlyph => ThemeManager.IsDark ? "\uE706" : "\uE708";

    public MainViewModel()
    {
        Cleaner = new CleanerViewModel();
        Dashboard = new DashboardViewModel(Cleaner);
        Ram = new RamViewModel();
        Settings = new SettingsViewModel();

        Dashboard.NavigateToCleaner = () => NavIndex = 1;
        CurrentPage = Dashboard;

        // Sidebar labels translate live; without this they keep the startup language.
        Loc.I.LanguageChanged += () => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            foreach (var item in NavItems)
                item.Raise(nameof(item.Label));
        });

        ThemeManager.ThemeChanged += () => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            Raise(nameof(ThemeGlyph));
        });

        ElevateCommand = new RelayCommand(FileOps.RestartAsAdministrator);
        ToggleLanguageCommand = new RelayCommand(() =>
        {
            Loc.I.SetLanguage(Loc.I.Language == "fa" ? "en" : "fa");
            SettingsService.Save();
        });
        ToggleThemeCommand = new RelayCommand(ThemeManager.ToggleTheme);
    }
}
