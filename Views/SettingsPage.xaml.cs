using System.Windows;
using System.Windows.Controls;
using Pakban.Localization;
using Pakban.Services;
using Pakban.ViewModels;

namespace Pakban.Views;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            SyncLangRadios();
            SyncThemeRadios();
            Loc.I.LanguageChanged += SyncLangRadios;
            ThemeManager.ThemeChanged += SyncThemeRadios;
        };
        Unloaded += (_, _) =>
        {
            Loc.I.LanguageChanged -= SyncLangRadios;
            ThemeManager.ThemeChanged -= SyncThemeRadios;
        };
    }

    private SettingsViewModel? VM => DataContext as SettingsViewModel;

    private void SyncLangRadios()
    {
        var target = Loc.I.Language == "fa" ? LangFa : LangEn;
        if (target is not null) target.IsChecked = true;
    }

    private void SyncThemeRadios()
    {
        if (ThemeManager.IsSystem)
        {
            if (ThemeSystem is not null) ThemeSystem.IsChecked = true;
        }
        else if (ThemeManager.IsDark)
        {
            if (ThemeDark is not null) ThemeDark.IsChecked = true;
        }
        else
        {
            if (ThemeLight is not null) ThemeLight.IsChecked = true;
        }
    }

    private void OnLangEn(object sender, RoutedEventArgs e) => VM?.SelectLanguage("en");
    private void OnLangFa(object sender, RoutedEventArgs e) => VM?.SelectLanguage("fa");

    private void OnThemeSystem(object sender, RoutedEventArgs e) => VM?.SelectTheme("System");
    private void OnThemeDark(object sender, RoutedEventArgs e) => VM?.SelectTheme("Dark");
    private void OnThemeLight(object sender, RoutedEventArgs e) => VM?.SelectTheme("Light");

    private void OnAccentClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Primitives.ButtonBase { Tag: string name })
            VM?.SelectAccent(name);
    }
}
