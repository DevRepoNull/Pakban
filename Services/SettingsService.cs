using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Serilog;

namespace Pakban.Services;

public sealed class AppSettings
{
    public string Language { get; set; } = "fa";
    public string Accent { get; set; } = "Teal";
    public string Theme { get; set; } = "System";
    public bool ConfirmBeforeClean { get; set; } = true;
}

/// <summary>
/// SettingsManager provides JSON persistence for user preferences in AppData/Roaming/Pakban/settings.json.
/// Also accessible via SettingsService.
/// </summary>
public static class SettingsManager
{
    public static AppSettings Current
    {
        get => SettingsService.Current;
        set => SettingsService.Current = value;
    }

    public static AppSettings Load() => SettingsService.Load();

    public static void Save() => SettingsService.Save();
}

public static class SettingsService
{
    public static AppSettings Current { get; set; } = new();

    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pakban");

    private static string FilePath => Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            // SECURITY FIX: Prevent Symlink / Junction reading attack (CWE-59).
            // Verify settings directory and file are not reparse points before reading.
            if (FileOps.IsReparsePoint(Dir) || FileOps.IsReparsePoint(FilePath))
            {
                Log.Warning("SECURITY FIX: Reparse point detected at settings location. Using default configuration.");
                return new AppSettings();
            }

            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    Log.Information("Settings loaded successfully from {FilePath}", FilePath);
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load settings from {FilePath}; using defaults", FilePath);
        }
        return new AppSettings();
    }

    public static void Save()
    {
        try
        {
            // SECURITY FIX: Prevent Arbitrary File Overwrite / Privilege Escalation via Symlink (CWE-59).
            // If an unprivileged user created a symlink from %APPDATA%\Pakban\settings.json to a sensitive system file,
            // an elevated Pakban process writing settings would overwrite the system file.
            if (FileOps.IsReparsePoint(Dir))
            {
                Log.Error("SECURITY FIX: Settings directory is a reparse point! Refusing to save to prevent link redirection.");
                return;
            }

            if (File.Exists(FilePath) && FileOps.IsReparsePoint(FilePath))
            {
                Log.Error("SECURITY FIX: settings.json is a symbolic link! Deleting the malicious link before writing.");
                File.Delete(FilePath); // Delete the link itself, never follow
            }

            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(Current, Options);

            // Safe atomic write pattern
            var tempFile = Path.Combine(Dir, $"settings_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(tempFile, json);
            File.Move(tempFile, FilePath, overwrite: true);

            Log.Information("Settings saved successfully to {FilePath}", FilePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save settings to {FilePath}", FilePath);
        }
    }
}

/// <summary>Named accent presets; swapping them rewrites application-level brushes live.</summary>
public static class AccentTheme
{
    public sealed record Preset(string Name, string Label, byte R1, byte G1, byte B1, byte R2, byte G2, byte B2);

    public static readonly List<Preset> Presets = new()
    {
        new("Teal",    "#2DD4BF", 0x2D, 0xD4, 0xBF, 0x0E, 0xA5, 0xE9),
        new("Violet",  "#A78BFA", 0xA7, 0x8B, 0xFA, 0x63, 0x66, 0xF1),
        new("Emerald", "#34D399", 0x34, 0xD3, 0x99, 0x05, 0x96, 0x69),
        new("Rose",    "#FB7185", 0xFB, 0x71, 0x85, 0xE1, 0x1D, 0x48),
        new("Amber",   "#FBBF24", 0xFB, 0xBF, 0x24, 0xF9, 0x73, 0x16),
        new("Sky",     "#60A5FA", 0x60, 0xA5, 0xFA, 0x43, 0x6C, 0xF6),
    };

    public static Preset Find(string name) =>
        Presets.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? Presets[0];

    public static void Apply(string presetName) => Apply(Find(presetName));

    public static void Apply(Preset preset)
    {
        var resources = Application.Current.Resources;
        var main = Color.FromRgb(preset.R1, preset.G1, preset.B1);
        var second = Color.FromRgb(preset.R2, preset.G2, preset.B2);

        resources["AccentBrush"] = new SolidColorBrush(main);
        resources["AccentSoftBrush"] = new SolidColorBrush(Color.FromArgb(0x2E, preset.R1, preset.G1, preset.B1));
        resources["AccentSoftBrush2"] = new SolidColorBrush(Color.FromArgb(0x1A, preset.R1, preset.G1, preset.B1));

        var gradient = new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(main, 0.0),
                new GradientStop(second, 1.0),
            })
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        resources["AccentGradientBrush"] = gradient;
        SettingsService.Current.Accent = preset.Name;
    }
}
