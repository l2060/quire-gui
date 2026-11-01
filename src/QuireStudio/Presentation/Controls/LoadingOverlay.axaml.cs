using Avalonia;
using Avalonia.Controls;

namespace QuireStudio.Presentation.Controls;

/// <summary>
/// 加载遮罩：半透明背景 + 旋转指示 + 说明文字。
/// 显示期间遮罩会吃掉鼠标事件，避免用户在加载过程中误点其它控件。
/// </summary>
public sealed partial class LoadingOverlay : UserControl
{
    public static readonly StyledProperty<string> MessageProperty =
        AvaloniaProperty.Register<LoadingOverlay, string>(nameof(Message), "加载中…");

    public LoadingOverlay() => InitializeComponent();

    /// <summary>遮罩上显示的说明文字。</summary>
    public string Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }
}
