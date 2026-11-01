using Avalonia;
using Avalonia.Media;

namespace QuireStudio.Presentation.Controls;

/// <summary>
/// 一段可旋转的圆弧，用作加载指示。
///
/// <para>
/// Avalonia 没有为 <c>RenderTransform</c> 注册动画器，直接对它做关键帧会抛异常；
/// 这里改为动画一个普通的 <see cref="Angle"/> 双精度属性，再在回调里更新旋转矩阵。
/// </para>
/// </summary>
public sealed class SpinnerArc : Avalonia.Controls.Shapes.Path
{
    public static readonly StyledProperty<double> AngleProperty =
        AvaloniaProperty.Register<SpinnerArc, double>(nameof(Angle));

    public SpinnerArc()
    {
        Data = Geometry.Parse("M16 2.4 A13.6 13.6 0 0 1 29.6 16");
        StrokeLineCap = PenLineCap.Round;
        RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        RenderTransform = new RotateTransform();
    }

    /// <summary>旋转角度（度）。</summary>
    public double Angle
    {
        get => GetValue(AngleProperty);
        set => SetValue(AngleProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == AngleProperty && RenderTransform is RotateTransform rotate)
        {
            rotate.Angle = Angle;
        }
    }
}
