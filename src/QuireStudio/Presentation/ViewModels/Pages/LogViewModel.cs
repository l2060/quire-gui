using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuireStudio.Presentation.Infrastructure;

namespace QuireStudio.Presentation.ViewModels.Pages;

/// <summary>
/// 运行日志页：CLI 调用、错误与项目切换的完整流水，
/// 独立成页后可以长时间保留，也方便整段复制反馈问题。
/// </summary>
public sealed partial class LogViewModel : BusyPageViewModel
{
    public LogViewModel(IWorkspace workspace)
        : base(workspace)
    {
        Workspace.Log.Changed += OnLogChanged;
    }

    public override string Key => "log";

    public ActivityLog Log => Workspace.Log;

    public ObservableCollection<LogEntry> Entries => Workspace.Log.Entries;

    public bool HasEntries => Entries.Count > 0;

    [ObservableProperty]
    private string _hint = "";

    public override Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        OnPropertyChanged(nameof(Entries));
        OnPropertyChanged(nameof(HasEntries));
        return Task.CompletedTask;
    }

    /// <summary>把全部日志复制到剪贴板。</summary>
    [RelayCommand]
    private async Task CopyLogAsync()
    {
        if (Entries.Count == 0)
        {
            return;
        }

        string text = string.Join(
            Environment.NewLine,
            Entries.Select(entry => $"{entry.TimeText}  {entry.Message}"));

        await Workspace.Shell.CopyTextAsync(text).ConfigureAwait(true);
        Hint = $"已复制 {Entries.Count} 条日志";
    }

    [RelayCommand]
    private void ClearLog()
    {
        Workspace.Log.Clear();
        Hint = "日志已清空";
    }

    private void OnLogChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(HasEntries));

        if (Entries.Count > 0)
        {
            Hint = "";
        }
    }
}
