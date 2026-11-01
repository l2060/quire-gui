using System.Text.Json;

namespace QuireStudio.Infrastructure.Cli;

/// <summary>一次 CLI 调用的完整结果。</summary>
public sealed record CliResult
{
    public required IReadOnlyList<string> Arguments { get; init; }

    /// <summary>用于展示的命令行（密码会被遮蔽）。</summary>
    public string CommandText { get; init; } = "";

    /// <summary>仅当收到 <c>completed</c> 且退出码为 <c>0</c> 时为真。</summary>
    public bool Succeeded { get; init; }

    public int ExitCode { get; init; }

    public CliEventKind TerminalKind { get; init; } = CliEventKind.Unknown;

    public JsonElement? Result { get; init; }

    /// <summary>所有 <c>item</c> 事件，按输出顺序排列。</summary>
    public IReadOnlyList<CliEvent> Items { get; init; } = [];

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    /// <summary>stderr 诊断文本，失败时用于补充原因。</summary>
    public string Diagnostics { get; init; } = "";

    /// <summary>构造一个未能启动进程的结果。</summary>
    public static CliResult NotStarted(string message, IReadOnlyList<string> arguments, string? commandText = null) =>
        new()
        {
            Arguments = arguments,
            CommandText = commandText ?? BuildCommandText(arguments),
            Succeeded = false,
            ExitCode = -1,
            TerminalKind = CliEventKind.Unknown,
            ErrorCode = "process_start_failed",
            ErrorMessage = message,
        };

    public static string BuildCommandText(IReadOnlyList<string> arguments) =>
        "quire-cli" + (arguments.Count == 0 ? "" : " " + string.Join(' ', arguments));
}

/// <summary>查询类命令的返回值：要么带数据成功，要么带可读原因失败。</summary>
public sealed class CliOutcome<T>
    where T : class
{
    private CliOutcome()
    {
    }

    public bool Succeeded { get; private init; }

    public T? Value { get; private init; }

    public string? Error { get; private init; }

    public CliResult? Result { get; private init; }

    public static CliOutcome<T> Success(T value, CliResult result) =>
        new() { Succeeded = true, Value = value, Result = result };

    public static CliOutcome<T> Failure(string error, CliResult? result = null) =>
        new() { Succeeded = false, Error = error, Result = result };
}
