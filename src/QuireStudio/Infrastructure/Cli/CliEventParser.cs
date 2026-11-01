using System.Globalization;
using System.Text.Json;

namespace QuireStudio.Infrastructure.Cli;

/// <summary>
/// 解析 <c>quire-cli -progress</c> 的 NDJSON 输出。
/// 解析失败只影响当前行，调用方仍会继续排空管道，避免子进程阻塞。
/// </summary>
internal static class CliEventParser
{
    /// <summary>解析一行事件；失败时返回 <see langword="null"/> 并给出原因。</summary>
    public static CliEvent? Parse(string line, out string? error)
    {
        error = null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "CLI 事件不是 JSON 对象。";
                return null;
            }

            string type = ReadString(root, "type") ?? "";
            return type switch
            {
                "started" => new CliEvent
                {
                    Kind = CliEventKind.Started,
                    Command = ReadString(root, "command") ?? "",
                    RawLine = line,
                },
                "progress" => new CliEvent
                {
                    Kind = CliEventKind.Progress,
                    Progress = ReadProgress(root),
                    RawLine = line,
                },
                "item" => new CliEvent
                {
                    Kind = CliEventKind.Item,
                    ItemKind = ReadString(root, "kind"),
                    Item = ReadElement(root, "data")?.Clone(),
                    RawLine = line,
                },
                "completed" => new CliEvent
                {
                    Kind = CliEventKind.Completed,
                    Result = ReadElement(root, "result")?.Clone(),
                    RawLine = line,
                },
                "failed" => CreateFailure(line, root, CliEventKind.Failed),
                "canceled" => CreateFailure(line, root, CliEventKind.Canceled),
                _ => CreateUnknown(line, type),
            };
        }
        catch (JsonException exception)
        {
            error = $"无法解析 CLI 事件：{exception.Message}";
            return null;
        }
    }

    private static CliEvent CreateFailure(string line, JsonElement root, CliEventKind kind)
    {
        JsonElement? error = ReadElement(root, "error");
        return new CliEvent
        {
            Kind = kind,
            ErrorCode = error is { } value ? ReadString(value, "code") : null,
            ErrorMessage = error is { } message ? ReadString(message, "message") : null,
            RawLine = line,
        };
    }

    private static CliEvent CreateUnknown(string line, string type) => new()
    {
        Kind = CliEventKind.Unknown,
        Command = type,
        RawLine = line,
    };

    private static CliProgress ReadProgress(JsonElement root) => new()
    {
        Stage = ReadString(root, "stage") ?? "",
        CompletedBytes = ReadInt64(root, "completedBytes"),
        TotalBytes = ReadInt64(root, "totalBytes"),
        CompletedFiles = ReadInt32(root, "completedFiles"),
        TotalFiles = ReadInt32(root, "totalFiles"),
        ReadBytes = ReadInt64(root, "readBytes") ?? 0,
        WrittenBytes = ReadInt64(root, "writtenBytes") ?? 0,
        StageFraction = ReadDouble(root, "stageFraction"),
        StageEtaSeconds = ReadDouble(root, "stageEtaSeconds"),
        ElapsedSeconds = ReadDouble(root, "elapsedSeconds") ?? 0,
        StageElapsedSeconds = ReadDouble(root, "stageElapsedSeconds") ?? 0,
    };

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static JsonElement? ReadElement(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out JsonElement value) &&
        value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? value
            : null;

    private static int? ReadInt32(JsonElement element, string propertyName) =>
        ReadElement(element, propertyName) is { ValueKind: JsonValueKind.Number } value &&
        value.TryGetInt32(out int result)
            ? result
            : null;

    private static long? ReadInt64(JsonElement element, string propertyName) =>
        ReadElement(element, propertyName) is { ValueKind: JsonValueKind.Number } value &&
        value.TryGetInt64(out long result)
            ? result
            : null;

    private static double? ReadDouble(JsonElement element, string propertyName) =>
        ReadElement(element, propertyName) is { ValueKind: JsonValueKind.Number } value &&
        value.TryGetDouble(out double result) &&
        !double.IsNaN(result) &&
        !double.IsInfinity(result)
            ? result
            : null;

    /// <summary>把 ISO 8601 时间戳转换为 <see cref="DateTimeOffset"/>，解析失败时回退到当前时间。</summary>
    public static DateTimeOffset ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed)
            ? parsed.ToUniversalTime()
            : DateTimeOffset.UtcNow;
}
