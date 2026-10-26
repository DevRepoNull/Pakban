using System.Windows;
using System.Windows.Media;

namespace Pakban.Controls;

/// <summary>A slim rounded progress bar drawn in OnRender (fraction 0..1).</summary>
public class PercentBar : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(PercentBar),
        new PropertyMetadata(0.0, OnFractionChanged));

    private static void OnFractionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((PercentBar)d).InvalidateVisual();

    public static readonly DependencyProperty BarBrushProperty = DependencyProperty.Register(
        nameof(BarBrush), typeof(Brush), typeof(PercentBar),
        new PropertyMetadata(null, OnFractionChanged));

    public double Fraction
    {
        get => (double)GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    public Brush? BarBrush
    {
        get => (Brush?)GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = Math.Max(ActualWidth, 1);
        double h = Math.Max(ActualHeight, 1);
        double radius = h / 2;

        dc.DrawRoundedRectangle(TrackBrush, null, new Rect(0, 0, w, h), radius, radius);

        // OnRender never mirrors automatically — follow the ambient FlowDirection so
        // the fill grows from the leading edge (right side under RTL), exactly like
        // the built-in ProgressBar does.
        double fill = Math.Clamp(Fraction, 0, 1) * w;
        if (fill > 0.5)
        {
            var rect = new Rect(0, 0, fill, h);
            dc.DrawRoundedRectangle(BarBrush ?? FillBrush, null, rect, radius, radius);
        }
    }

    private static readonly Brush TrackBrush = new SolidColorBrush(Color.FromArgb(0x26, 0x64, 0x74, 0x8B));
    private static readonly Brush FillBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0xD4, 0xBF));
}
