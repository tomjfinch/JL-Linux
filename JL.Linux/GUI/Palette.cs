using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace JL.Linux.GUI;

internal static class Palette
{
    private static readonly Dictionary<(string, double), IBrush> s_cache = [];

    public static Color ParseColor(string hex, Color fallback)
    {
        return Color.TryParse(hex, out Color color) ? color : fallback;
    }

    public static IBrush Brush(string hex, double opacity = 1)
    {
        if (s_cache.TryGetValue((hex, opacity), out IBrush? cached))
        {
            return cached;
        }

        Color color = ParseColor(hex, Avalonia.Media.Colors.White);
        if (opacity < 1)
        {
            color = Color.FromArgb((byte)Math.Clamp(color.A * opacity, 0, 255), color.R, color.G, color.B);
        }

        IBrush brush = new ImmutableSolidColorBrush(color);
        s_cache[(hex, opacity)] = brush;
        return brush;
    }
}
