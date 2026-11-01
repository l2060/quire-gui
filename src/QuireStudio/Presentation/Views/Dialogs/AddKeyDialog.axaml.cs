using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using QuireStudio.Presentation.Infrastructure;
using QuireStudio.Presentation.ViewModels.Dialogs;

namespace QuireStudio.Presentation.Views.Dialogs;

/// <summary>「添加命名密钥」对话框。校验通过后由 <see cref="AddKeyDialogViewModel"/> 返回结果并关闭。</summary>
public sealed partial class AddKeyDialog : Window
{
    private readonly AddKeyDialogViewModel _viewModel;
    private bool _closing;

    public AddKeyDialog(AddKeyDialogViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        InitializeComponent();

        DataContext = viewModel;
        viewModel.Submitted += OnSubmitted;
        viewModel.Cancelled += OnCancelled;

        Opened += OnOpenedHandler;
    }

    private void OnOpenedHandler(object? sender, EventArgs e)
    {
        Opened -= OnOpenedHandler;
        NameBox.Focus();
    }

    private void OnSubmitted(object? sender, EventArgs e) => CloseOnce(_viewModel.CreateRequest());

    private void OnCancelled(object? sender, EventArgs e) => CloseOnce(null);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => CloseOnce(null);

    /// <summary>幂等关闭并延后一轮，避免窗口销毁后仍有输入事件回投。</summary>
    private void CloseOnce(NewKeyRequest? result)
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
