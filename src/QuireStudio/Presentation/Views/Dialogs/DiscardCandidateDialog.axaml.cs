using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using QuireStudio.Presentation.Infrastructure;
using QuireStudio.Presentation.ViewModels.Dialogs;

namespace QuireStudio.Presentation.Views.Dialogs;

/// <summary>「放弃候选」确认对话框：确认后返回是否一并删除编译输出。</summary>
public sealed partial class DiscardCandidateDialog : Window
{
    private readonly DiscardCandidateDialogViewModel _viewModel;
    private bool _closing;

    public DiscardCandidateDialog(DiscardCandidateDialogViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        InitializeComponent();

        DataContext = viewModel;
        viewModel.Confirmed += OnConfirmed;
        viewModel.Cancelled += OnCancelled;
    }

    private void OnConfirmed(object? sender, EventArgs e) => CloseOnce(_viewModel.CreateRequest());

    private void OnCancelled(object? sender, EventArgs e) => CloseOnce(null);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => CloseOnce(null);

    /// <summary>
    /// 幂等关闭，并延后到下一轮调度。
    ///
    /// <para>
    /// 直接在当前输入事件里 <c>Close</c> 会让窗口立刻销毁平台实现，
    /// 紧随其后的按键抬起等事件仍会回投到已分离的可视树，
    /// 触发 "PlatformImpl is null, couldn't handle input" 警告。
    /// </para>
    /// </summary>
    private void CloseOnce(DiscardCandidateRequest? result)
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        Dispatcher.UIThread.Post(() => Close(result));
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                CloseOnce(null);
                e.Handled = true;
                break;
            case Key.Enter:
                _viewModel.ConfirmCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}
