using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuireStudio.Presentation.Infrastructure;

namespace QuireStudio.Presentation.ViewModels.Dialogs;

/// <summary>
/// 「放弃候选」对话框的 ViewModel：只负责说明后果与收集"是否一并删除打包输出"，
/// 真正的移除与清理由打包分支页面在对话框关闭后执行。
/// </summary>
public sealed partial class DiscardCandidateDialogViewModel : ObservableObject
{
    public DiscardCandidateDialogViewModel(string tagName, int outputCount)
    {
        TagName = tagName;
        OutputCount = outputCount;
    }

    /// <summary>即将被放弃的候选名。</summary>
    public string TagName { get; }

    /// <summary>该候选记录的编译输出数量。</summary>
    public int OutputCount { get; }

    public bool HasOutputs => OutputCount > 0;

    public string OutputSummary => HasOutputs
        ? $"该候选记录了 {OutputCount} 个输出文件（位于项目的 dist 目录下）。"
        : "该候选没有输出文件，无需清理磁盘。";

    /// <summary>是否同时删除打包输出；默认勾选，符合"放弃候选即清理产物"的预期。</summary>
    [ObservableProperty]
    private bool _deleteOutputs = true;

    /// <summary>用户确认放弃。</summary>
    public event EventHandler? Confirmed;

    /// <summary>用户取消。</summary>
    public event EventHandler? Cancelled;

    [RelayCommand]
    private void Confirm() => Confirmed?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);

    /// <summary>把选择打包成对话框返回值。</summary>
    public DiscardCandidateRequest CreateRequest() => new(DeleteOutputs && HasOutputs);
}
