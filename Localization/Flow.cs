using System.Windows;

namespace Pakban.Localization;

/// <summary>
/// Flow-relative horizontal margins.
/// In WPF layout, Left acts as the leading (Start) edge and Right acts as the
/// trailing (End) edge within mirrored flow panels.
/// This helper safely manages Left (Start) and Right (End) margins without being
/// wiped out by XAML defaults.
/// </summary>
public static class Flow
{
    public static readonly DependencyProperty StartProperty = DependencyProperty.RegisterAttached(
        "Start", typeof(double), typeof(Flow), new PropertyMetadata(double.NaN, OnMarginChanged));

    public static readonly DependencyProperty EndProperty = DependencyProperty.RegisterAttached(
        "End", typeof(double), typeof(Flow), new PropertyMetadata(double.NaN, OnMarginChanged));

    private static readonly DependencyProperty IsHookedProperty = DependencyProperty.RegisterAttached(
        "IsHooked", typeof(bool), typeof(Flow), new PropertyMetadata(false));

    public static void SetStart(DependencyObject d, double value) => d.SetValue(StartProperty, value);
    public static double GetStart(DependencyObject d) => (double)d.GetValue(StartProperty);

    public static void SetEnd(DependencyObject d, double value) => d.SetValue(EndProperty, value);
    public static double GetEnd(DependencyObject d) => (double)d.GetValue(EndProperty);

    private static void OnMarginChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;

        UpdateMargin(fe);

        if (!(bool)fe.GetValue(IsHookedProperty))
        {
            fe.SetValue(IsHookedProperty, true);
            fe.Loaded += (s, _) => UpdateMargin(fe);
        }
    }

    public static void UpdateMargin(FrameworkElement fe)
    {
        double start = GetStart(fe);
        double end = GetEnd(fe);

        if (double.IsNaN(start) && double.IsNaN(end)) return;

        double s = double.IsNaN(start) ? 0 : start;
        double en = double.IsNaN(end) ? 0 : end;

        Thickness current = fe.Margin;
        if (Math.Abs(current.Left - s) > 0.001 || Math.Abs(current.Right - en) > 0.001)
        {
            fe.Margin = new Thickness(s, current.Top, en, current.Bottom);
        }
    }
}
