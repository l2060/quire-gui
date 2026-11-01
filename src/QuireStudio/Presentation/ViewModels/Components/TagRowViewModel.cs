using Avalonia.Media;
using QuireStudio.Domain;
using QuireStudio.Domain.History;

namespace QuireStudio.Presentation.ViewModels.Components;

/// <summary>历史图列表中的一行。</summary>
public sealed class TagRowViewModel
{
    private const double LaneSpacing = 20d;
    private const double LeftPadding = 15d;
    private const double RightPadding = 10d;

    public TagRowViewModel(TagGraphRow row, int laneCount)
    {
        ArgumentNullException.ThrowIfNull(row);

        GraphRow = row;
        GutterWidth = LeftPadding + (Math.Max(laneCount, row.Lane + 1) * LaneSpacing) + RightPadding;
    }

    public TagGraphRow GraphRow { get; }

    public QuireTag Tag => GraphRow.Tag;

    /// <summary>图形列宽度，随泳道数量自适应。</summary>
    public double GutterWidth { get; }

    public string Name => Tag.TagName;

    public string StatusText => StatusStyle.Text(Tag.Status);

    public IBrush StatusForeground => StatusStyle.Foreground(Tag.Status);

    public IBrush StatusBackground => StatusStyle.Background(Tag.Status);

    public string BuildTimeText => Tag.BuildTimeLocal.ToString("MM-dd HH:mm");

    public string AddedText => $"+{Tag.AddedFiles}";

    public string UpdatedText => $"~{Tag.UpdatedFiles}";

    public string RemovedText => $"-{Tag.RemovedFiles}";

    public string OutputSummary => Tag.Outputs.Count == 0
        ? "无输出文件"
        : $"{Tag.Outputs.Count} 个资源包";

    public bool IsBaseline => Tag.IsBaseline;

    public bool IsCandidate => Tag.IsCandidate;

    /// <summary>已提交（非基线、非候选）。</summary>
    public bool IsCommitted => Tag.Status == TagStatus.Committed;

    public bool IsRemoved => Tag.Status == TagStatus.Removed;
}
