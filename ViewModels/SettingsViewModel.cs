using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using Pakban.Localization;
using Pakban.Services;

namespace Pakban.ViewModels;

public class LanguageOptionVM : ViewModelBase
{
    public required string Code { get; init; }
    public string Label => Loc.T(Code == "fa" ? "set_lang_fa" : "set_lang_en");
    public bool IsCurrent => Loc.I.Language == Code;
}

public class AccentOptionVM : ViewModelBase
{
    public required string Name { get; init; }
    public Brush Swatch { get; init; } = Brushes.Teal;
    public bool IsCurrent { get; set; }
}

public class SettingsViewModel : ViewModelBase
{
    public ObservableCollection<LanguageOptionVM> Languages { get; } = new()
    {
        new LanguageOptionVM { Code = "en" },
        new LanguageOptionVM { Code = "fa" },
    };

    public ObservableCollection<AccentOptionVM> Accents { get; } = new();

    private bool _confirmBeforeClean;
    public bool ConfirmBeforeClean
    {
        get => _confirmBeforeClean;
        set { if (Set(ref _confirmBeforeClean, value)) { SettingsService.Current.ConfirmBeforeClean = value; SettingsService.Save(); } }
    }

    public bool IsSystemTheme => ThemeManager.IsSystem;
    public bool IsDarkTheme => ThemeManager.CurrentTheme == ThemeManager.Dark;
    public bool IsLightTheme => ThemeManager.CurrentTheme == ThemeManager.Light;

    public string VersionText => "1.0.0";

    public SettingsViewModel()
    {
        foreach (var preset in AccentTheme.Presets)
        {
            var vm = new AccentOptionVM
            {
                Name = preset.Name,
                Swatch = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(System.Windows.Media.Color.FromRgb(preset.R1, preset.G1, preset.B1), 0.0),
                        new GradientStop(System.Windows.Media.Color.FromRgb(preset.R2, preset.G2, preset.B2), 1.0),
                    })
                {
                    StartPoint = new System.Windows.Point(0, 0),
                    EndPoint = new System.Windows.Point(1, 1),
                },
            };
            Accents.Add(vm);
        }
        SyncSelections();

        _confirmBeforeClean = SettingsService.Current.ConfirmBeforeClean;

        Loc.I.LanguageChanged += () => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            foreach (var l in Languages) l.Raise(nameof(l.Label));
        });

        ThemeManager.ThemeChanged += () => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            Raise(nameof(IsSystemTheme));
            Raise(nameof(IsDarkTheme));
            Raise(nameof(IsLightTheme));
        });
    }

    public void SelectTheme(string theme)
    {
        ThemeManager.ApplyTheme(theme);
        Raise(nameof(IsSystemTheme));
        Raise(nameof(IsDarkTheme));
        Raise(nameof(IsLightTheme));
    }

    public void SelectLanguage(string code)
    {
        Loc.I.SetLanguage(code);
        SettingsService.Save();
        SyncSelections();
    }

    public void SelectAccent(string name)
    {
        AccentTheme.Apply(name);
        SettingsService.Save();
        SyncSelections();
    }

    private void SyncSelections()
    {
        foreach (var l in Languages) l.Raise(nameof(l.IsCurrent));
        foreach (var a in Accents) a.IsCurrent = a.Name.Equals(SettingsService.Current.Accent, StringComparison.OrdinalIgnoreCase);
        foreach (var a in Accents) a.Raise(nameof(a.IsCurrent));
    }
}
