using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using QuireStudio.Domain;
using QuireStudio.Domain.History;

namespace QuireStudio.Presentation.Controls;

/// <summary>
/// Tag 历史图的单行绘制层。每行只绘制自己的连线片段与节点，
/// 因此可以直接放进 ItemsControl，不需要维护整图坐标或对齐滚动。
/// </summary>
public sealed class TagGraphGutter : Control
{
    private const double LaneSpacing = 20d;
    private const double LeftPadding = 15d;
    private const double LineThickness = 1.6d;

    public static readonly StyledProperty<TagGraphRow?> RowProperty =
        AvaloniaProperty.Register<TagGraphGutter, TagGraphRow?>(nameof(Row));

    /// <summary>节点内部的填充色应与所在卡片一致，因此由外部按主题传入。</summary>
    public static readonly StyledProperty<IBrush?> BackdropProperty =
        AvaloniaProperty.Register<TagGraphGutter, IBrush?>(nameof(Backdrop));

    static TagGraphGutter()
    {
        AffectsRender<TagGraphGutter>(RowProperty);
        AffectsRender<TagGraphGutter>(BackdropProperty);
    }

    /// <summary>本行要绘制的图数据。</summary>
    public TagGraphRow? Row
    {
        get => GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    /// <summary>节点内部填充所依赖的背景色（随主题变化）。</summary>
    public IBrush? Backdrop
    {
        get => GetValue(BackdropProperty);
        set => SetValue(BackdropProperty, value);
    }

    private Color BackdropColor =>
        Backdrop is ISolidColorBrush solid ? solid.Color : Color.Parse("#101620");

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Row is not { } row || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        double height = Bounds.Height;
        double middle = height / 2d;
        var fromTop = new HashSet<int>(row.LinesFromTop);
        var toBottom = new HashSet<int>(row.LinesToBottom);

        // 贯通整行的泳道：上下两段都画，视觉上是一条直线。
        foreach (int lane in fromTop.Intersect(toBottom).Order())
        {
            DrawVertical(context, lane, 0, height);
        }

        // 只出现在上半行的泳道：直行到节点，或弯折并入本行节点。
        foreach (int lane in fromTop.Except(toBottom).Order())
        {
            if (lane == row.Lane)
            {
                DrawVertical(context, lane, 0, middle);
                continue;
            }

            double bend = middle - Math.Max(middle * 0.35, 6d);
            DrawVertical(context, lane, 0, bend);
            DrawCurve(context, lane, bend, row.Lane, middle);
        }

        // 只出现在下半行的泳道：从节点或贯通位置继续向下。
        foreach (int lane in toBottom.Except(fromTop).Order())
        {
            DrawVertical(context, lane, middle, height);
        }

        DrawNode(context, row, middle);
    }

    private static double X(int lane) => LeftPadding + (lane * LaneSpacing);

    private static Color LaneColor(int lane) =>
        Palette.Lane(lane) is ISolidColorBrush solid ? solid.Color : Colors.MediumPurple;

    private static IBrush LaneBrush(int lane) => Palette.Lane(lane);

    private void DrawVertical(DrawingContext context, int lane, double top, double bottom)
    {
        if (bottom <= top)
        {
            return;
        }

        var pen = new Pen(LaneBrush(lane), LineThickness, lineCap: PenLineCap.Round);
        context.DrawLine(pen, new Point(X(lane), top), new Point(X(lane), bottom));
    }

    private void DrawCurve(DrawingContext context, int fromLane, double fromY, int toLane, double toY)
    {
        var pen = new Pen(LaneBrush(fromLane), LineThickness, lineCap: PenLineCap.Round);

        double startX = X(fromLane);
        double endX = X(toLane);
        double controlOffset = Math.Max((toY - fromY) * 0.55, 1d);

        var geometry = new StreamGeometry();
        using (StreamGeometryContext path = geometry.Open())
        {
            path.BeginFigure(new Point(startX, fromY), isFilled: false);
            path.CubicBezierTo(
                new Point(startX, fromY + controlOffset),
                new Point(endX, toY - controlOffset),
                new Point(endX, toY));
            path.EndFigure(isClosed: false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private void DrawNode(DrawingContext context, TagGraphRow row, double middle)
    {
        var center = new Point(X(row.Lane), middle);
        IBrush laneBrush = LaneBrush(row.Lane);
        IBrush tintedBrush = new SolidColorBrush(Blend(BackdropColor, LaneColor(row.Lane), 0.22));

        switch (row.Tag.Status)
        {
            case TagStatus.Baseline:
                using (context.PushOpacity(0.18))
                {
                    context.DrawEllipse(laneBrush, null, center, 10.5, 10.5);
                }

                context.DrawEllipse(tintedBrush, null, center, 6.2, 6.2);
                context.DrawEllipse(null, new Pen(laneBrush, 1.7), center, 6.2, 6.2);
                context.DrawEllipse(laneBrush, null, center, 2.9, 2.9);
                break;

            case TagStatus.Candidate:
                context.DrawEllipse(tintedBrush, null, center, 5.6, 5.6);
                context.DrawEllipse(null, new Pen(laneBrush, 2), center, 5, 5);
                break;

            case TagStatus.Removed:
                context.DrawEllipse(tintedBrush, new Pen(Palette.TextTertiary, 1.6), center, 4.6, 4.6);
                break;

            default:
                context.DrawEllipse(laneBrush, null, center, 5, 5);
                break;
        }
    }

    private static Color Blend(Color background, Color foreground, double amount) =>
        Color.FromRgb(
            (byte)(background.R + ((foreground.R - background.R) * amount)),
            (byte)(background.G + ((foreground.G - background.G) * amount)),
            (byte)(background.B + ((foreground.B - background.B) * amount)));
}
