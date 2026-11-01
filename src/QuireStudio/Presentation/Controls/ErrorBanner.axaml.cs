using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace QuireStudio.Presentation.Controls;

/// <summary>
/// 页面底部的错误提示条。绑定所在页面的 ViewModel：
/// 显示 <c>ErrorMessage</c>，并提供复制与关闭操作。
/// </summary>
public sealed partial class ErrorBanner : UserControl
{
    public ErrorBanner()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
