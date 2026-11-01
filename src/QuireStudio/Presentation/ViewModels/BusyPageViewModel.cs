using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace QuireStudio.Presentation.ViewModels;

/// <summary>带加载状态与错误提示的页面基类。</summary>
public abstract partial class BusyPageViewModel : PageViewModel
{
    protected BusyPageViewModel(IWorkspace workspace)
        : base(workspace)
    {
    }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    /// <summary>把错误信息复制到剪贴板，便于反馈问题。</summary>
    [RelayCommand]
    private async Task CopyErrorAsync()
    {
        if (string.IsNullOrWhiteSpace(ErrorMessage))
        {
            return;
        }

        await Workspace.Shell.CopyTextAsync(ErrorMessage).ConfigureAwait(true);
        Workspace.Log.Info("错误信息已复制到剪贴板。");
    }

    /// <summary>关闭错误提示。</summary>
    [RelayCommand]
    private void DismissError() => ErrorMessage = null;

    /// <summary>
    /// 在文件管理器中打开目录；系统不支持时把路径复制到剪贴板并提示，
    /// 避免用户面对一个"点了没反应"的按钮。
    /// </summary>
    protected async Task OpenFolderAsync(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        if (Workspace.Shell.TryOpenInFileManager(directory))
        {
            return;
        }

        await Workspace.Shell.CopyTextAsync(directory).ConfigureAwait(true);
        ReportFailure($"系统无法打开文件管理器，路径已复制到剪贴板：{directory}", "打开目录失败。");
    }

    /// <summary>清空错误提示。</summary>
    protected void ClearError() => ErrorMessage = null;

    /// <summary>记录一次失败，同时写入活动日志。</summary>
    protected void ReportFailure(string? message, string fallback)
    {
        string text = string.IsNullOrWhiteSpace(message) ? fallback : message;
        ErrorMessage = text;
        Workspace.Log.Error(text);
    }
}
