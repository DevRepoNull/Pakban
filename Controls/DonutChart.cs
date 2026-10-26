using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Pakban.Controls;

public sealed class DonutSegment
{
    public required double Value { get; init; }
    public required Brush Brush { get; init; }
    public required string Label { get; init; }
    public string Detail { get; init; } = "";
}

/// <summary>
/// An animated donut chart drawn in OnRender — no third-party chart library.
/// The geometry is deliberately flow-invariant (a donut is symmetric, so RTL must
/// not mirror it). Center text is NOT rendered here on purpose: text belongs to
/// XAML, where it follows the app font and language automatically — overlay a
/// centered TextBlock in the page instead.
/// </summary>
public class DonutChart : FrameworkElement
{
    static DonutChart()
    {
        FlowDirectionProperty.OverrideMetadata(typeof(DonutChart), new FrameworkPropertyMetadata(FlowDirection.LeftToRight));
    }

    public DonutChart()
    {
        FlowDirection = FlowDirection.LeftToRight;
    }
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments), typeof(IReadOnlyList<DonutSegment>), typeof(DonutChart),
        new PropertyMetadata(null, OnSegmentsChanged));

    public static readonly DependencyProperty AnimProgressProperty = DependencyProperty.Register(
        nameof(AnimProgress), typeof(double), typeof(DonutChart), new PropertyMetadata(0.0, OnVisualChanged));

    public IReadOnlyList<DonutSegment>? Segments
    {
        get => (IReadOnlyList<DonutSegment>?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    private double AnimProgress
    {
        get => (double)GetValue(AnimProgressProperty);
        set => SetValue(AnimProgressProperty, value);
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((DonutChart)d).InvalidateVisual();

    private static void OnSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (DonutChart)d;
        var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(650))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd,
        };
        chart.BeginAnimation(AnimProgressProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private (double Start, double End, DonutSegment Seg)[]? _hitRanges;

    protected override void OnRender(DrawingContext dc)
    {
        double width = Math.Max(ActualWidth, 1);
        double height = Math.Max(ActualHeight, 1);
        var center = new Point(width / 2, height / 2);
        double outer = Math.Min(width, height) / 2 - 2;
        double inner = outer * 0.64;

        var track = (Brush)(Application.Current.Resources["ChartTrackBrush"] ?? Brushes.Transparent);
        DrawRing(dc, center, outer, inner, track);

        var segments = Segments;
        _hitRanges = null;
        double total = segments?.Sum(s => Math.Max(0, s.Value)) ?? 0;

        if (segments is not null && total > 0 && AnimProgress > 0)
        {
            var ranges = new List<(double, double, DonutSegment)>();
            double angle = -90;
            foreach (var seg in segments)
            {
                if (seg.Value <= 0) continue;
                double sweep = 360.0 * seg.Value / total * AnimProgress;
                if (sweep > 0.05)
                {
                    double end = angle + Math.Min(sweep, 359.999);
                    dc.DrawGeometry(seg.Brush, null, BuildSegment(center, outer, inner, angle, end));
                    ranges.Add((angle, end, seg));
                }
                angle += sweep;
                if (angle >= 270) break;
            }
            _hitRanges = ranges.ToArray();
        }
    }

    private static void DrawRing(DrawingContext dc, Point center, double outer, double inner, Brush brush)
    {
        var pen = new Pen(brush, outer - inner);
        pen.Freeze();
        double radius = (outer + inner) / 2;
        dc.DrawEllipse(null, pen, center, radius, radius);
    }

    private static StreamGeometry BuildSegment(Point center, double outer, double inner, double startDeg, double endDeg)
    {
        double a0 = startDeg * Math.PI / 180;
        double a1 = endDeg * Math.PI / 180;
        var p0 = Polar(center, outer, a0);
        var p1 = Polar(center, outer, a1);
        var p2 = Polar(center, inner, a1);
        var p3 = Polar(center, inner, a0);
        bool large = endDeg - startDeg > 180;

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(p0, isFilled: true, isClosed: true);
            ctx.ArcTo(p1, new Size(outer, outer), 0, large, SweepDirection.Clockwise, true, false);
            ctx.LineTo(p2, true, false);
            ctx.ArcTo(p3, new Size(inner, inner), 0, large, SweepDirection.Counterclockwise, true, false);
            ctx.LineTo(p0, true, false);
        }
        geo.Freeze();
        return geo;
    }

    private static Point Polar(Point center, double radius, double radians)
        => new(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_hitRanges is null || _hitRanges.Length == 0) return;

        var pos = e.GetPosition(this);
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double dx = pos.X - center.X;
        double dy = pos.Y - center.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        double outer = Math.Min(ActualWidth, ActualHeight) / 2 - 2;
        double inner = outer * 0.64;

        if (dist < inner || dist > outer)
        {
            ToolTip = null;
            return;
        }

        double deg = Math.Atan2(dy, dx) * 180 / Math.PI; // -180..180
        if (deg < -90) deg += 360;

        foreach (var (start, end, seg) in _hitRanges)
        {
            if (deg >= start - 0.5 && deg <= end + 0.5)
            {
                ToolTip = string.IsNullOrEmpty(seg.Detail) ? seg.Label : $"{seg.Label} — {seg.Detail}";
                return;
            }
        }
        ToolTip = null;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        ToolTip = null;
        base.OnMouseLeave(e);
    }
}
