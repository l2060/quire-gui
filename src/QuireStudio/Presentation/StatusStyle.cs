using Avalonia.Media;
using QuireStudio.Domain;

namespace QuireStudio.Presentation;

/// <summary>Tag 状态的显示文案与配色，供列表与详情复用。</summary>
public static class StatusStyle
{
    public static string Text(TagStatus status) => status switch
    {
        TagStatus.Baseline => "基线",
        TagStatus.Committed => "已提交",
        TagStatus.Candidate => "候选",
        TagStatus.Removed => "已移除",
        _ => "未知",
    };

    public static IBrush Foreground(TagStatus status) => status switch
    {
        TagStatus.Baseline => Palette.Success,
        TagStatus.Committed => Palette.TextSecondary,
        TagStatus.Candidate => Palette.Warning,
        TagStatus.Removed => Palette.Danger,
        _ => Palette.TextTertiary,
    };

    public static IBrush Background(TagStatus status) => status switch
    {
        TagStatus.Baseline => Palette.SuccessSoft,
        TagStatus.Committed => Palette.SurfaceRaised,
        TagStatus.Candidate => Palette.WarningSoft,
        TagStatus.Removed => Palette.DangerSoft,
        _ => Palette.SurfaceRaised,
    };

    /// <summary>变化类型的显示文案。</summary>
    public static string Text(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => "新增",
        ChangeKind.Updated => "更新",
        ChangeKind.Deleted => "删除",
        _ => "变更",
    };

    public static IBrush Foreground(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => Palette.Success,
        ChangeKind.Updated => Palette.Warning,
        ChangeKind.Deleted => Palette.Danger,
        _ => Palette.TextSecondary,
    };

    public static IBrush Background(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => Palette.SuccessSoft,
        ChangeKind.Updated => Palette.WarningSoft,
        ChangeKind.Deleted => Palette.DangerSoft,
        _ => Palette.SurfaceRaised,
    };
}
