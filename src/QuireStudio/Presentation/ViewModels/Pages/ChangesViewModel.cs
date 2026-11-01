using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuireStudio.Domain;
using QuireStudio.Infrastructure.Cli;
using QuireStudio.Presentation.ViewModels.Components;

namespace QuireStudio.Presentation.ViewModels.Pages;

/// <summary>
/// 基线变更页：当前源文件相对正式基线的增删改。
///
/// <para>
/// 差异可能有几万甚至几十万条，因此列表只按页生成行对象：
/// 过滤结果保存在内存里（轻量模型），界面按块追加，未加载的部分不会产生控件。
/// </para>
/// </summary>
public sealed partial class ChangesViewModel : BusyPageViewModel
{
    /// <summary>每次生成的行数。</summary>
    private const int PageSize = 400;

    private readonly List<QuireChange> _all = [];
    private List<QuireChange> _filtered = [];

    public ChangesViewModel(IWorkspace workspace)
        : base(workspace)
    {
    }

    public override string Key => "changes";

    /// <summary>当前已生成的可见行。</summary>
    public ObservableCollection<ChangeRowViewModel> Changes { get; } = [];

    /// <summary>当前筛选：<c>all</c> / <c>added</c> / <c>updated</c> / <c>deleted</c>。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilterAll))]
    [NotifyPropertyChangedFor(nameof(IsFilterAdded))]
    [NotifyPropertyChangedFor(nameof(IsFilterUpdated))]
    [NotifyPropertyChangedFor(nameof(IsFilterDeleted))]
    private string _filter = "all";

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private int _addedCount;

    [ObservableProperty]
    private int _updatedCount;

    [ObservableProperty]
    private int _deletedCount;

    [ObservableProperty]
    private string _baselineName = "—";

    [ObservableProperty]
    private string _emptyHint = "计算差异后在此查看结果";

    public int TotalCount => AddedCount + UpdatedCount + DeletedCount;

    public bool HasChanges => Changes.Count > 0;

    public bool CanLoadMoreChanges => Changes.Count < _filtered.Count;

    public string LoadMoreText =>
        $"显示更多（还有 {_filtered.Count - Changes.Count:N0} 条，共 {_filtered.Count:N0} 条）";

    public bool IsFilterAll => Filter == "all";

    public bool IsFilterAdded => Filter == "added";

    public bool IsFilterUpdated => Filter == "updated";

    public bool IsFilterDeleted => Filter == "deleted";

    /// <summary>执行 <c>diff</c>，比较当前源文件与基线。</summary>
    [RelayCommand]
    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (Workspace.Project is not { } project)
        {
            Clear();
            return;
        }

        IsBusy = true;
        ClearError();
        EmptyHint = "正在比较源文件…";

        try
        {
            CliOutcome<IReadOnlyList<QuireChange>> outcome = await Workspace.Cli
                .DiffAsync(project, cancellationToken: cancellationToken)
                .ConfigureAwait(true);

            if (!outcome.Succeeded)
            {
                Clear();
                ReportFailure(outcome.Error, "计算差异失败。");
                return;
            }

            _all.Clear();
            _all.AddRange(outcome.Value!);

            AddedCount = _all.Count(change => change.Kind == ChangeKind.Added);
            UpdatedCount = _all.Count(change => change.Kind == ChangeKind.Updated);
            DeletedCount = _all.Count(change => change.Kind == ChangeKind.Deleted);
            BaselineName = Workspace.Configuration?.DisplayName ?? "当前基线";

            ApplyFilter();
            Workspace.Log.Info($"差异计算完成：{AddedCount} 新增 / {UpdatedCount} 更新 / {DeletedCount} 删除");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public override void OnProjectChanged()
    {
        Clear();
    }

    [RelayCommand]
    private void SetFilter(string? filter)
    {
        Filter = string.IsNullOrWhiteSpace(filter) ? "all" : filter;
        ApplyFilter();
    }

    /// <summary>追加一页变更行。</summary>
    [RelayCommand(CanExecute = nameof(CanLoadMoreChanges))]
    private void LoadMore() => AppendChunk();

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        IEnumerable<QuireChange> query = _all;

        query = Filter switch
        {
            "added" => query.Where(change => change.Kind == ChangeKind.Added),
            "updated" => query.Where(change => change.Kind == ChangeKind.Updated),
            "deleted" => query.Where(change => change.Kind == ChangeKind.Deleted),
            _ => query,
        };

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string keyword = SearchText.Trim();
            query = query.Where(change =>
                change.Path.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                change.Package.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        _filtered = query as List<QuireChange> ?? query.ToList();

        Changes.Clear();
        AppendChunk();

        EmptyHint = _all.Count == 0
            ? "当前源文件与基线一致"
            : "没有符合筛选条件的变化";
    }

    private void AppendChunk()
    {
        int start = Changes.Count;
        int end = Math.Min(_filtered.Count, start + PageSize);

        for (int index = start; index < end; index++)
        {
            Changes.Add(new ChangeRowViewModel(_filtered[index]));
        }

        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(CanLoadMoreChanges));
        OnPropertyChanged(nameof(LoadMoreText));
        LoadMoreCommand.NotifyCanExecuteChanged();
    }

    private void Clear()
    {
        _all.Clear();
        _filtered = [];
        Changes.Clear();
        AddedCount = 0;
        UpdatedCount = 0;
        DeletedCount = 0;
        EmptyHint = "计算差异后在此查看结果";

        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(CanLoadMoreChanges));
        OnPropertyChanged(nameof(LoadMoreText));
        LoadMoreCommand.NotifyCanExecuteChanged();
        ClearError();
    }
}
