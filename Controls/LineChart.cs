using System.Windows;
using System.Windows.Media;

namespace Pakban.Controls;

/// <summary>A lightweight live line chart (0..100 scale) for RAM history.</summary>
public class LineChart : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IReadOnlyList<double>), typeof(LineChart),
        new PropertyMetadata(null, OnPointsChanged));

    public IReadOnlyList<double>? Points
    {
        get => (IReadOnlyList<double>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    private static void OnPointsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((LineChart)d).InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        double width = Math.Max(ActualWidth, 1);
        double height = Math.Max(ActualHeight, 1);

        var gridPen = new Pen(GridBrush, 1);
        gridPen.Freeze();
        for (int i = 1; i <= 4; i++)
        {
            double y = height * i / 5;
            dc.DrawLine(gridPen, new Point(0, y), new Point(width, y));
        }

        var points = Points;
        if (points is null || points.Count < 2) return;

        double stepX = width / Math.Max(points.Count - 1, 1);

        double Y(int i)
        {
            double v = Math.Clamp(points[i], 0, 100);
            return height - (v / 100.0) * (height - 8) - 4;
        }

        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(new Point(0, Y(0)), false, false);
            for (int i = 1; i < points.Count; i++)
                ctx.LineTo(new Point(i * stepX, Y(i)), true, false);
        }
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(0, height), true, true);
            for (int i = 0; i < points.Count; i++)
                ctx.LineTo(new Point(i * stepX, Y(i)), true, false);
            ctx.LineTo(new Point(width, height), true, false);
        }
        line.Freeze();
        area.Freeze();

        dc.DrawGeometry(AreaBrush, null, area);

        var linePen = new Pen(LineBrush, 2.2)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        linePen.Freeze();
        dc.DrawGeometry(null, linePen, line);

        dc.DrawEllipse(LineBrush, null, new Point(width, Y(points.Count - 1)), 3.5, 3.5);
    }

    private static readonly Brush GridBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x33, 0x41, 0x55));
    private static readonly Brush LineBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0xD4, 0xBF));
    private static readonly Brush AreaBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x2D, 0xD4, 0xBF));
}
