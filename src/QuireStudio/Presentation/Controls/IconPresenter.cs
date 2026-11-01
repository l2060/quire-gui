using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace QuireStudio.Presentation.Controls;

/// <summary>
/// 以 24×24 视图框绘制线性图标，按 <see cref="Size"/> 等比缩放并居中。
/// 相比在每个图标处嵌套 Viewbox，这里只做一次绘制，测量也更稳定。
/// </summary>
public sealed class IconPresenter : Control
{
    private const double ViewBoxSize = 24d;

    public static readonly StyledProperty<Geometry?> SourceProperty =
        AvaloniaProperty.Register<IconPresenter, Geometry?>(nameof(Source));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<IconPresenter, IBrush?>(nameof(Stroke), Brushes.White);

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<IconPresenter, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<IconPresenter, double>(nameof(StrokeThickness), 1.9d);

    static IconPresenter()
    {
        AffectsRender<IconPresenter>(SourceProperty, StrokeProperty, FillProperty, StrokeThicknessProperty);
        AffectsMeasure<IconPresenter>(SourceProperty);
    }

    /// <summary>图标几何数据，坐标系为 24×24。</summary>
    public Geometry? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>描边画刷。</summary>
    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>可选的填充画刷，用于实心标记。</summary>
    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Source is not { } source || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        double scale = Math.Min(Bounds.Width, Bounds.Height) / ViewBoxSize;
        var transform =
            Matrix.CreateScale(scale, scale) *
            Matrix.CreateTranslation(
                (Bounds.Width - (ViewBoxSize * scale)) / 2d,
                (Bounds.Height - (ViewBoxSize * scale)) / 2d);

        var pen = Stroke is null || StrokeThickness <= 0
            ? null
            : new Pen(Stroke, StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

        using (context.PushTransform(transform))
        {
            context.DrawGeometry(Fill, pen, source);
        }
    }
}
