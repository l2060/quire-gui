using Avalonia.Controls;
using Avalonia.Threading;
using QuireStudio.Presentation.ViewModels.Pages;

namespace QuireStudio.Presentation.Views.Pages;

/// <summary>运行日志页：新日志到达时自动滚到底部。</summary>
public sealed partial class LogView : UserControl
{
    private LogViewModel? _viewModel;

    public LogView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.Log.Changed -= OnLogChanged;
        }

        _viewModel = DataContext as LogViewModel;

        if (_viewModel is not null)
        {
            _viewModel.Log.Changed += OnLogChanged;
        }
    }

    private void OnLogChanged(object? sender, EventArgs e)
    {
        if (LogList.ItemCount == 0)
        {
            return;
        }

        // 等布局完成后再滚动，否则容器还没生成。
        Dispatcher.UIThread.Post(
            () => LogList.ScrollIntoView(LogList.ItemCount - 1),
            DispatcherPriority.Background);
    }
}
