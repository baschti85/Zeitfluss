using System.Windows;
using Zeitfluss.Models;

namespace Zeitfluss.Services;

public static class WindowAppearance
{
    public const double MinimumOpacityPercent = 80;
    public const double MaximumOpacityPercent = 100;
    public const double DefaultOpacityPercent = 92;

    public static double NormalizePercent(double value)
    {
        if (!double.IsFinite(value)) return DefaultOpacityPercent;
        return Math.Clamp(Math.Round(value), MinimumOpacityPercent, MaximumOpacityPercent);
    }

    public static double ToOpacity(double percent) => NormalizePercent(percent) / 100d;

    public static void Apply(Window window, AppSettings settings) =>
        window.Opacity = ToOpacity(settings.WindowOpacityPercent);
}
