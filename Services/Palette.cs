using System.Windows.Media;

namespace Pakban.Services;

/// <summary>Fixed color palette used across categories, charts and legends.</summary>
public static class Palette
{
    private static readonly Color[] Colors =
    {
        From("#2DD4BF"), From("#38BDF8"), From("#A78BFA"), From("#FB923C"),
        From("#F472B6"), From("#FACC15"), From("#4ADE80"), From("#F87171"),
        From("#C084FC"), From("#22D3EE"), From("#FDBA74"), From("#94A3B8"),
        From("#E879F9"), From("#FCA5A5"), From("#86EFAC"),
    };

    private static readonly Dictionary<int, SolidColorBrush> Cache = new();

    public static Brush For(int index)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(index, out var brush)) return brush;
            var color = Colors[Math.Abs(index) % Colors.Length];
            var solid = new SolidColorBrush(color);
            solid.Freeze();
            Cache[index] = solid;
            return solid;
        }
    }

    public static Brush Danger { get; } = Frozen("#F87171");
    public static Brush Warning { get; } = Frozen("#FBBF24");

    private static Color From(string hex) =>
        Color.FromRgb(
            Convert.ToByte(hex[1..3], 16),
            Convert.ToByte(hex[3..5], 16),
            Convert.ToByte(hex[5..7], 16));

    private static SolidColorBrush Frozen(string hex)
    {
        var b = new SolidColorBrush(From(hex));
        b.Freeze();
        return b;
    }
}
