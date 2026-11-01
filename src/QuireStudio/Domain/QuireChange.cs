namespace QuireStudio.Domain;

/// <summary>资源相对基线的变化类型。</summary>
public enum ChangeKind
{
    Added,
    Updated,
    Deleted,
}

/// <summary>
/// 单条资源变化记录，来自 <c>quire-cli diff</c>（当前源文件）或 <c>changes NAME</c>（历史构建）。
/// </summary>
public sealed record QuireChange
{
    public required string Package { get; init; }

    public required string Path { get; init; }

    public required ChangeKind Kind { get; init; }

    /// <summary>用于表格徽标的单字符标识：<c>A</c> / <c>M</c> / <c>D</c>。</summary>
    public char Badge => Kind switch
    {
        ChangeKind.Added => 'A',
        ChangeKind.Updated => 'M',
        ChangeKind.Deleted => 'D',
        _ => '?',
    };

    /// <summary>包内路径的最后一段，用于紧凑展示。</summary>
    public string FileName
    {
        get
        {
            int separator = Path.LastIndexOf('/');
            return separator < 0 ? Path : Path[(separator + 1)..];
        }
    }
}
