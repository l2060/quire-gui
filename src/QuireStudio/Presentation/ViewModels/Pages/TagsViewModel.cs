using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuireStudio.Domain;
using QuireStudio.Domain.History;
using QuireStudio.Infrastructure.Cli;
using QuireStudio.Infrastructure.Storage;
using QuireStudio.Presentation.Infrastructure;
using QuireStudio.Presentation.ViewModels.Components;

namespace QuireStudio.Presentation.ViewModels.Pages;

/// <summary>
/// 打包分支页：把 CLI 的 Tag 历史渲染成 Git 风格的分支图，
/// 并提供打包候选、提交基线、放弃候选三个核心操作。
/// </summary>
public sealed partial class TagsViewModel : BusyPageViewModel
{
    /// <summary>每次“显示更多”追加的行数。十万级变更不能一次性做成控件。</summary>
    private const int ChangePageSize = 300;

    private CancellationTokenSource? _operationCts;
    private CancellationTokenSource? _changesCts;
    private string? _pendingSelectionTagId;

    /// <summary>当前 Tag 的全部变更（轻量模型，只保留字符串）。</summary>
    private IReadOnlyList<QuireChange> _allChanges = [];

    public TagsViewModel(IWorkspace workspace)
        : base(workspace)
    {
    }

    public override string Key => "tags";

    /// <summary>历史图的行，按构建时间倒序。</summary>
    public ObservableCollection<TagRowViewModel> Rows { get; } = [];

    /// <summary>当前选中 Tag 在构建时记录的文件变化。</summary>
    public ObservableCollection<ChangeRowViewModel> SelectedChanges { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscardCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenOutputFolderCommand))]
    private TagRowViewModel? _selectedRow;

    /// <summary>用户填写的候选名称；留空表示由 CLI 自动生成。</summary>
    [ObservableProperty]
    private string _newTagName = "";

    [ObservableProperty]
    private bool _isPackaging;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private bool _isProgressIndeterminate = true;

    [ObservableProperty]
    private string _progressStage = "";

    [ObservableProperty]
    private string _progressDetail = "";

    [ObservableProperty]
    private bool _isLoadingChanges;

    public int TagCount => Rows.Count;

    public int CandidateCount => Rows.Count(row => row.IsCandidate);

    public bool HasTags => Rows.Count > 0;

    public bool HasSelection => SelectedRow is not null;

    public bool IsSelectedCandidate => SelectedRow?.Tag.Status == TagStatus.Candidate;

    public string DetailName => SelectedRow?.Name ?? "";

    public string DetailTagId => SelectedRow?.Tag.TagId ?? "";

    public string DetailTime => SelectedRow is { } row
        ? row.Tag.BuildTimeLocal.ToString("yyyy-MM-dd HH:mm:ss")
        : "";

    public string DetailParent => ResolveParentName(SelectedRow?.Tag.ParentTagId);

    public IBrush DetailStatusForeground => SelectedRow is { } row
        ? row.StatusForeground
        : Palette.TextTertiary;

    public IBrush DetailStatusBackground => SelectedRow is { } row
        ? row.StatusBackground
        : Palette.SurfaceRaised;

    public string DetailStatusText => SelectedRow?.StatusText ?? "";

    // 详情区胶囊的样式类：这里统一给出布尔值，避免 XAML 绑定 SelectedRow.X 时
    // 因为选中项为空而刷出 "Value is null" 的绑定错误。
    public bool DetailIsBaseline => SelectedRow?.IsBaseline == true;

    public bool DetailIsCandidate => SelectedRow?.IsCandidate == true;

    public bool DetailIsCommitted => SelectedRow?.IsCommitted == true;

    public bool DetailIsRemoved => SelectedRow?.IsRemoved == true;

    public string DetailAdded => SelectedRow?.Tag.AddedFiles.ToString() ?? "0";

    public string DetailUpdated => SelectedRow?.Tag.UpdatedFiles.ToString() ?? "0";

    public string DetailRemoved => SelectedRow?.Tag.RemovedFiles.ToString() ?? "0";

    public string DetailContentChanges => SelectedRow?.Tag.ContentChanges.ToString() ?? "0";

    public string DetailMetadataChanges => SelectedRow?.Tag.MetadataChanges.ToString() ?? "0";

    public string DetailOutputs => SelectedRow is not { } row || row.Tag.Outputs.Count == 0
        ? "—"
        : string.Join(Environment.NewLine, row.Tag.Outputs);

    public string DetailOutputCount => SelectedRow is { } row && row.Tag.Outputs.Count > 0
        ? $"{row.Tag.Outputs.Count} 个资源包"
        : "无输出文件";

    public string ChangesSummary => SelectedRow is null
        ? "选择一条记录查看变更"
        : _allChanges.Count == 0
            ? "本次没有文件变化"
            : SelectedChanges.Count >= _allChanges.Count
                ? $"共 {_allChanges.Count:N0} 条变化"
                : $"已显示 {SelectedChanges.Count:N0} / {_allChanges.Count:N0} 条变化";

    /// <summary>是否还有未生成的变更行。</summary>
    public bool CanLoadMoreChanges => SelectedChanges.Count < _allChanges.Count;

    public string LoadMoreChangesText =>
        $"显示更多（还有 {_allChanges.Count - SelectedChanges.Count:N0} 条）";

    public bool CanPackage => !IsPackaging && Workspace.Project is not null;

    /// <summary>读取 Tag 历史并重建分支图。</summary>
    [RelayCommand]
    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (Workspace.Project is not { } project)
        {
            Reset();
            return;
        }

        IsBusy = true;
        ClearError();
        try
        {
            SelectedRow = null;
            CliOutcome<IReadOnlyList<QuireTag>> outcome = await Workspace.Cli
                .GetTagsAsync(project, cancellationToken: cancellationToken)
                .ConfigureAwait(true);

            if (!outcome.Succeeded)
            {
                Rows.Clear();
                ReportFailure(outcome.Error, "读取 Tag 历史失败。");
                return;
            }

            RebuildRows(outcome.Value!);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>构建候选；基线保持不变。</summary>
    [RelayCommand(CanExecute = nameof(CanPackage))]
    private async Task PackageAsync()
    {
        if (Workspace.Project is not { } project)
        {
            return;
        }

        string? requestedName = string.IsNullOrWhiteSpace(NewTagName) ? null : NewTagName.Trim();

        _operationCts = new CancellationTokenSource();
        IsPackaging = true;
        IsProgressIndeterminate = true;
        ProgressPercent = 0;
        ProgressStage = "准备中";
        ProgressDetail = "";
        ClearError();
        Workspace.Log.Info($"开始打包：{requestedName ?? "自动命名"}");

        try
        {
            CliOutcome<QuireTag> outcome = await Workspace.Cli
                .PackageAsync(
                    project,
                    requestedName,
                    OnCliEvent,
                    _operationCts.Token)
                .ConfigureAwait(true);

            if (!outcome.Succeeded)
            {
                ReportFailure(outcome.Error, "打包失败。");
                return;
            }

            QuireTag tag = outcome.Value!;
            _pendingSelectionTagId = tag.TagId;
            NewTagName = "";
            Workspace.Log.Success(
                $"候选 {tag.TagName} 已生成：{tag.AddedFiles} 新增 / {tag.UpdatedFiles} 更新 / {tag.RemovedFiles} 删除");
        }
        catch (OperationCanceledException)
        {
            Workspace.Log.Warning("打包已取消。");
        }
        finally
        {
            _operationCts.Dispose();
            _operationCts = null;
            IsPackaging = false;
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>把候选提交为正式基线。</summary>
    [RelayCommand(CanExecute = nameof(IsSelectedCandidate))]
    private async Task CommitAsync()
    {
        if (Workspace.Project is not { } project || SelectedRow is not { } row)
        {
            return;
        }

        ClearError();
        Workspace.Log.Info($"提交基线：{row.Name}");
        CliOutcome<QuireTag> outcome = await Workspace.Cli
            .CommitAsync(project, row.Tag.TagName)
            .ConfigureAwait(true);

        if (!outcome.Succeeded)
        {
            ReportFailure(outcome.Error, "提交失败。");
            return;
        }

        _pendingSelectionTagId = row.Tag.TagId;
        Workspace.Log.Success($"{row.Name} 已成为正式基线。");
        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>放弃未提交的候选；是否一并删除打包输出由用户在确认框里决定。</summary>
    [RelayCommand(CanExecute = nameof(IsSelectedCandidate))]
    private async Task DiscardAsync()
    {
        if (Workspace.Project is not { } project || SelectedRow is not { } row)
        {
            return;
        }

        ClearError();

        DiscardCandidateRequest? request = await Workspace.Shell
            .ShowDiscardCandidateDialogAsync(row.Name, row.Tag.Outputs.Count)
            .ConfigureAwait(true);

        if (request is null)
        {
            return;
        }

        Workspace.Log.Info($"放弃候选：{row.Name}");
        CliOutcome<QuireTag> outcome = await Workspace.Cli
            .DiscardAsync(project, row.Tag.TagName)
            .ConfigureAwait(true);

        if (!outcome.Succeeded)
        {
            ReportFailure(outcome.Error, "放弃候选失败。");
            return;
        }

        if (request.DeleteOutputs)
        {
            (int deleted, int failed) = ProjectOutputCleaner.Clean(project.Directory, row.Tag.Outputs);

            if (failed > 0)
            {
                Workspace.Log.Warning($"候选 {row.Name} 已移除，但 {failed} 个输出文件删除失败。");
            }
            else
            {
                Workspace.Log.Success($"候选 {row.Name} 已移除，并删除 {deleted} 个输出文件。");
            }
        }
        else
        {
            Workspace.Log.Success($"候选 {row.Name} 已移除，输出文件保留。");
        }

        _pendingSelectionTagId = null;
        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>取消正在进行的打包。</summary>
    [RelayCommand]
    private void Cancel() => _operationCts?.Cancel();

    /// <summary>在文件管理器中打开当前 Tag 的输出目录。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenOutputs))]
    private async Task OpenOutputFolderAsync()
    {
        if (SelectedRow is not { } row || row.Tag.Outputs.Count == 0)
        {
            return;
        }

        string baseDirectory = Workspace.Configuration?.Directory ?? Workspace.Project?.Directory ?? "";
        string firstOutput = row.Tag.Outputs[0];
        string fullPath = Path.IsPathRooted(firstOutput)
            ? firstOutput
            : Path.Combine(baseDirectory, firstOutput);

        string target = File.Exists(fullPath) ? Path.GetDirectoryName(fullPath) ?? fullPath : fullPath;
        string directory = Directory.Exists(target) ? target : baseDirectory;

        await OpenFolderAsync(directory).ConfigureAwait(true);
    }

    private bool CanOpenOutputs => SelectedRow?.Tag.Outputs.Count > 0;

    public override async Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        if (Rows.Count == 0 && !IsBusy)
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    public override void OnProjectChanged()
    {
        Reset();
    }

    private void Reset()
    {
        _pendingSelectionTagId = null;
        Rows.Clear();
        SelectedRow = null;
        SelectedChanges.Clear();
        _allChanges = [];
        NotifyChangesPaging();
        CandidateCountChanged();
        ClearError();
    }

    private void RebuildRows(IReadOnlyList<QuireTag> tags)
    {
        string? preferredTagId = _pendingSelectionTagId ?? SelectedRow?.Tag.TagId;
        _pendingSelectionTagId = null;

        TagGraph graph = TagGraphBuilder.Build(tags);

        Rows.Clear();
        foreach (TagGraphRow row in graph.Rows)
        {
            Rows.Add(new TagRowViewModel(row, graph.LaneCount));
        }

        TagRowViewModel? next = preferredTagId is null
            ? null
            : Rows.FirstOrDefault(row => row.Tag.TagId == preferredTagId);

        SelectedRow = next ?? Rows.FirstOrDefault();

        OnPropertyChanged(nameof(TagCount));
        OnPropertyChanged(nameof(HasTags));
        CandidateCountChanged();
    }

    private void CandidateCountChanged()
    {
        OnPropertyChanged(nameof(CandidateCount));
        OnPropertyChanged(nameof(CanPackage));
        PackageCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedRowChanged(TagRowViewModel? value)
    {
        NotifyDetailChanged();
        _ = LoadSelectedChangesAsync();
    }

    private void NotifyDetailChanged()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsSelectedCandidate));
        OnPropertyChanged(nameof(DetailName));
        OnPropertyChanged(nameof(DetailTagId));
        OnPropertyChanged(nameof(DetailTime));
        OnPropertyChanged(nameof(DetailParent));
        OnPropertyChanged(nameof(DetailStatusForeground));
        OnPropertyChanged(nameof(DetailStatusBackground));
        OnPropertyChanged(nameof(DetailStatusText));
        OnPropertyChanged(nameof(DetailIsBaseline));
        OnPropertyChanged(nameof(DetailIsCandidate));
        OnPropertyChanged(nameof(DetailIsCommitted));
        OnPropertyChanged(nameof(DetailIsRemoved));
        OnPropertyChanged(nameof(DetailAdded));
        OnPropertyChanged(nameof(DetailUpdated));
        OnPropertyChanged(nameof(DetailRemoved));
        OnPropertyChanged(nameof(DetailContentChanges));
        OnPropertyChanged(nameof(DetailMetadataChanges));
        OnPropertyChanged(nameof(DetailOutputs));
        OnPropertyChanged(nameof(DetailOutputCount));
    }

    private async Task LoadSelectedChangesAsync()
    {
        _changesCts?.Cancel();
        _changesCts?.Dispose();
        _changesCts = null;

        SelectedChanges.Clear();
        _allChanges = [];
        NotifyChangesPaging();

        if (Workspace.Project is not { } project || SelectedRow is not { } row)
        {
            IsLoadingChanges = false;
            return;
        }

        var cts = new CancellationTokenSource();
        _changesCts = cts;
        IsLoadingChanges = true;

        try
        {
            CliOutcome<IReadOnlyList<QuireChange>> outcome = await Workspace.Cli
                .GetTagChangesAsync(project, row.Tag.TagName, cancellationToken: cts.Token)
                .ConfigureAwait(true);

            if (cts.IsCancellationRequested)
            {
                return;
            }

            if (!outcome.Succeeded)
            {
                ReportFailure(outcome.Error, "读取变更记录失败。");
                return;
            }

            _allChanges = outcome.Value!;
            AppendChangeChunk();
        }
        catch (OperationCanceledException)
        {
            // 快速切换选中项时取消上一次查询。
        }
        finally
        {
            if (!cts.IsCancellationRequested)
            {
                IsLoadingChanges = false;
            }
        }
    }

    /// <summary>追加一页变更行；只有已加载的部分才会生成 UI 容器。</summary>
    private void AppendChangeChunk()
    {
        int start = SelectedChanges.Count;
        int end = Math.Min(_allChanges.Count, start + ChangePageSize);

        for (int index = start; index < end; index++)
        {
            SelectedChanges.Add(new ChangeRowViewModel(_allChanges[index]));
        }

        NotifyChangesPaging();
    }

    private void NotifyChangesPaging()
    {
        OnPropertyChanged(nameof(ChangesSummary));
        OnPropertyChanged(nameof(CanLoadMoreChanges));
        OnPropertyChanged(nameof(LoadMoreChangesText));
        LoadMoreChangesCommand.NotifyCanExecuteChanged();
    }

    /// <summary>追加显示更多变更（避免十万级记录一次性生成控件）。</summary>
    [RelayCommand(CanExecute = nameof(CanLoadMoreChanges))]
    private void LoadMoreChanges() => AppendChangeChunk();

    private void OnCliEvent(CliEvent cliEvent)
    {
        if (cliEvent.Kind == CliEventKind.Progress && cliEvent.Progress is { } progress)
        {
            UiDispatcher.Post(() => ApplyProgress(progress));
        }
    }

    private void ApplyProgress(CliProgress progress)
    {
        ProgressStage = Formatting.Stage(progress.Stage);
        IsProgressIndeterminate = progress.StageFraction is null;
        ProgressPercent = (progress.StageFraction ?? 0) * 100;

        if (progress.CompletedFiles is { } files)
        {
            ProgressDetail = progress.TotalFiles is { } totalFiles
                ? $"{Formatting.Number(files)} / {Formatting.Number(totalFiles)} 个文件"
                : $"{Formatting.Number(files)} 个文件";
        }
        else if (progress.CompletedBytes is { } bytes)
        {
            ProgressDetail = progress.TotalBytes is { } totalBytes
                ? $"{Formatting.Bytes(bytes)} / {Formatting.Bytes(totalBytes)}"
                : Formatting.Bytes(bytes);
        }
        else
        {
            ProgressDetail = "";
        }
    }

    private string ResolveParentName(string? parentTagId)
    {
        if (string.IsNullOrWhiteSpace(parentTagId))
        {
            return "— 首次构建";
        }

        TagRowViewModel? parent = Rows.FirstOrDefault(row => row.Tag.TagId == parentTagId);
        return parent?.Name ?? parentTagId;
    }

    partial void OnIsPackagingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanPackage));
        PackageCommand.NotifyCanExecuteChanged();
    }
}
