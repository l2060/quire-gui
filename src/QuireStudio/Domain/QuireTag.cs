namespace QuireStudio.Domain;

/// <summary>
/// 资源包构建记录的状态。与 <c>quire-cli</c> 的 <c>tags</c>、<c>commit</c>、<c>remove</c> 结果一一对应。
/// </summary>
public enum TagStatus
{
    /// <summary>尚未提交的候选构建，可继续验证或放弃。</summary>
    Candidate,

    /// <summary>已提交但已被更新的基线取代，保留在历史链中。</summary>
    Committed,

    /// <summary>当前正式基线，增量打包以它为比较目标。</summary>
    Baseline,

    /// <summary>已删除的候选（仅出现在 <c>remove</c> 的返回结果中）。</summary>
    Removed,
}

/// <summary>
/// 一次资源包构建记录，对应 <c>quire-cli tags</c> 输出的单条 <c>tag</c> 记录。
/// </summary>
public sealed record QuireTag
{
    public required string TagId { get; init; }

    public required string TagName { get; init; }

    /// <summary>构建所基于的 Tag；首次构建为 <see langword="null"/>。</summary>
    public string? ParentTagId { get; init; }

    public TagStatus Status { get; init; }

    public DateTimeOffset BuildTimeUtc { get; init; }

    /// <summary>内容发生变化的资源数量。</summary>
    public int ContentChanges { get; init; }

    /// <summary>仅 Meta 发生变化的资源数量。</summary>
    public int MetadataChanges { get; init; }

    /// <summary>被删除的资源数量。</summary>
    public int DeletedChanges { get; init; }

    public int AddedFiles { get; init; }

    public int UpdatedFiles { get; init; }

    public int RemovedFiles { get; init; }

    /// <summary>本次构建生成的包路径，相对项目配置所在目录。</summary>
    public IReadOnlyList<string> Outputs { get; init; } = [];

    /// <summary>新增、更新与删除资源的总数。</summary>
    public int TotalFileChanges => AddedFiles + UpdatedFiles + RemovedFiles;

    public DateTime BuildTimeLocal => BuildTimeUtc.ToLocalTime().DateTime;

    public bool IsBaseline => Status == TagStatus.Baseline;

    public bool IsCandidate => Status == TagStatus.Candidate;
}
