using System.Text.Json;

namespace QuireStudio.Infrastructure.Cli;

/// <summary><c>quire-cli -progress</c> 输出的事件类型。</summary>
public enum CliEventKind
{
    /// <summary>无法识别的行。</summary>
    Unknown,

    Started,
    Progress,
    Item,
    Completed,
    Failed,
    Canceled,
}

/// <summary>一次进度快照，字段含义遵循 <c>docs/cli-protocol.md</c>。</summary>
public sealed record CliProgress
{
    public string Stage { get; init; } = "";

    public long? CompletedBytes { get; init; }

    public long? TotalBytes { get; init; }

    public int? CompletedFiles { get; init; }

    public int? TotalFiles { get; init; }

    public long ReadBytes { get; init; }

    public long WrittenBytes { get; init; }

    /// <summary>当前阶段完成比例；总量未知时为 <see langword="null"/>。</summary>
    public double? StageFraction { get; init; }

    public double? StageEtaSeconds { get; init; }

    public double ElapsedSeconds { get; init; }

    public double StageElapsedSeconds { get; init; }
}

/// <summary>单条结构化事件。</summary>
public sealed record CliEvent
{
    public CliEventKind Kind { get; init; } = CliEventKind.Unknown;

    /// <summary><c>started</c> 事件中的顶层命令名。</summary>
    public string Command { get; init; } = "";

    public CliProgress? Progress { get; init; }

    /// <summary><c>item</c> 事件的记录类型：<c>tag</c>、<c>change</c>、<c>key</c>、<c>package</c>、<c>asset</c>。</summary>
    public string? ItemKind { get; init; }

    public JsonElement? Item { get; init; }

    /// <summary><c>completed</c> 事件的最终结果。</summary>
    public JsonElement? Result { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public string RawLine { get; init; } = "";

    /// <summary>是否为终态事件。</summary>
    public bool IsTerminal => Kind is CliEventKind.Completed or CliEventKind.Failed or CliEventKind.Canceled;
}
