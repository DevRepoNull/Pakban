using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Pakban.Services;

/// <summary>
/// Manages application-wide Light, Dark, and System (Windows-adaptive) theme switching,
/// updating both the MergedDictionaries and root-level dynamic brush resources live at runtime.
/// </summary>
public static class ThemeManager
{
    public const string SystemMode = "System";
    public const string Dark = "Dark";
    public const string Light = "Light";

    private static bool _initialized;

    /// <summary>
    /// Stored theme configuration: "System", "Dark", or "Light". Defaults to "System".
    /// </summary>
    public static string CurrentTheme =>
        string.IsNullOrWhiteSpace(SettingsService.Current.Theme) ? SystemMode : SettingsService.Current.Theme;

    /// <summary>Whether the application is configured to follow the Windows system theme.</summary>
    public static bool IsSystem => CurrentTheme.Equals(SystemMode, StringComparison.OrdinalIgnoreCase);

    /// <summary>Actual theme currently displayed ("Dark" or "Light").</summary>
    public static string EffectiveTheme =>
        IsSystem ? GetWindowsTheme() : (CurrentTheme.Equals(Light, StringComparison.OrdinalIgnoreCase) ? Light : Dark);

    public static bool IsDark => EffectiveTheme.Equals(Dark, StringComparison.OrdinalIgnoreCase);
    public static bool IsLight => EffectiveTheme.Equals(Light, StringComparison.OrdinalIgnoreCase);

    public static event Action? ThemeChanged;

    /// <summary>
    /// Initializes OS theme change listener (UserPreferenceChanged) to react live
    /// when Windows switches between Light and Dark mode.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }
        catch { /* best effort */ }
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
        {
            if (IsSystem)
            {
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    ApplyTheme(SystemMode, save: false);
                });
            }
        }
    }

    /// <summary>
    /// Detects Windows system theme setting from the registry.
    /// HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize -> AppsUseLightTheme
    /// </summary>
    public static string GetWindowsTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var val = key?.GetValue("AppsUseLightTheme");
            if (val is int intVal)
            {
                return intVal > 0 ? Light : Dark;
            }
        }
        catch { }

        // Default fallback if registry cannot be read
        return Dark;
    }

    /// <summary>
    /// Applies a theme setting ("System", "Dark", or "Light").
    /// In "System" mode, it dynamically queries Windows theme and sets up auto-tracking.
    /// In "Dark" or "Light" mode, it pins the manual user choice.
    /// </summary>
    public static void ApplyTheme(string? themeMode, bool save = true)
    {
        string chosenMode = string.IsNullOrWhiteSpace(themeMode) ? SystemMode : themeMode;

        if (chosenMode.Equals(Light, StringComparison.OrdinalIgnoreCase))
            chosenMode = Light;
        else if (chosenMode.Equals(Dark, StringComparison.OrdinalIgnoreCase))
            chosenMode = Dark;
        else
            chosenMode = SystemMode;

        SettingsService.Current.Theme = chosenMode;

        // Resolve which visual dictionary to load (Dark or Light)
        string targetDict = chosenMode == SystemMode ? GetWindowsTheme() : chosenMode;

        var app = Application.Current;
        if (app is not null)
        {
            var dictUri = new Uri($"Themes/{targetDict}.xaml", UriKind.Relative);
            var newDict = new ResourceDictionary { Source = dictUri };

            // 1. Swap theme dictionary in MergedDictionaries
            var merged = app.Resources.MergedDictionaries;
            var existingIndex = -1;
            for (int i = 0; i < merged.Count; i++)
            {
                if (merged[i].Source?.OriginalString.StartsWith("Themes/", StringComparison.OrdinalIgnoreCase) == true)
                {
                    existingIndex = i;
                    break;
                }
            }

            if (existingIndex >= 0)
            {
                merged[existingIndex] = newDict;
            }
            else
            {
                merged.Add(newDict);
            }

            // 2. Direct copy of theme brushes into app.Resources for high-priority live updates
            foreach (var key in newDict.Keys)
            {
                if (newDict[key] is Brush brush)
                {
                    app.Resources[key] = brush;
                }
            }

            // 3. Re-apply active accent preset on top of the resolved theme
            AccentTheme.Apply(SettingsService.Current.Accent);
        }

        if (save)
        {
            SettingsService.Save();
        }

        ThemeChanged?.Invoke();
    }

    /// <summary>
    /// Toggles manually between Light and Dark mode, pinning the user's explicit choice.
    /// </summary>
    public static void ToggleTheme()
    {
        ApplyTheme(IsDark ? Light : Dark, save: true);
    }
}
