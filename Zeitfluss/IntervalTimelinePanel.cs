using System.Windows;

namespace Zeitfluss;

/// <summary>
/// Arranges timeline segments on a responsive 24-hour axis.
/// </summary>
public sealed class IntervalTimelinePanel : System.Windows.Controls.Panel
{
    public static readonly DependencyProperty StartMinuteProperty = DependencyProperty.RegisterAttached(
        "StartMinute",
        typeof(double),
        typeof(IntervalTimelinePanel),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty EndMinuteProperty = DependencyProperty.RegisterAttached(
        "EndMinute",
        typeof(double),
        typeof(IntervalTimelinePanel),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsArrange));

    public static void SetStartMinute(DependencyObject element, double value) =>
        element.SetValue(StartMinuteProperty, value);

    public static double GetStartMinute(DependencyObject element) =>
        (double)element.GetValue(StartMinuteProperty);

    public static void SetEndMinute(DependencyObject element, double value) =>
        element.SetValue(EndMinuteProperty, value);

    public static double GetEndMinute(DependencyObject element) =>
        (double)element.GetValue(EndMinuteProperty);

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize)
    {
        var height = double.IsInfinity(availableSize.Height) ? 22d : availableSize.Height;
        foreach (UIElement child in InternalChildren)
            child.Measure(new System.Windows.Size(double.PositiveInfinity, height));

        return new System.Windows.Size(double.IsInfinity(availableSize.Width) ? 0d : availableSize.Width, height);
    }

    protected override System.Windows.Size ArrangeOverride(System.Windows.Size finalSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            var start = Math.Clamp(GetStartMinute(child), 0d, 1440d);
            var end = Math.Clamp(GetEndMinute(child), start, 1440d);
            var left = finalSize.Width * start / 1440d;
            var naturalWidth = finalSize.Width * (end - start) / 1440d;
            var width = Math.Min(Math.Max(naturalWidth, 3d), Math.Max(0d, finalSize.Width - left));
            child.Arrange(new Rect(left, 0d, width, finalSize.Height));
        }

        return finalSize;
    }
}
