using System.Text.Json;
using QuireStudio.Domain;

namespace QuireStudio.Infrastructure.Cli;

/// <summary>把 CLI 的 JSON 记录映射为领域模型。</summary>
internal static class CliJsonParser
{
    public static QuireTag ToTag(JsonElement? element)
    {
        JsonElement value = element ?? default;
        return new QuireTag
        {
            TagId = ReadString(value, "tagId") ?? "",
            TagName = ReadString(value, "tagName") ?? "",
            ParentTagId = ReadString(value, "parentTagId"),
            Status = ParseStatus(ReadString(value, "status")),
            BuildTimeUtc = CliEventParser.ParseTimestamp(ReadString(value, "buildTimeUtc")),
            ContentChanges = ReadInt32(value, "contentChanges"),
            MetadataChanges = ReadInt32(value, "metadataChanges"),
            DeletedChanges = ReadInt32(value, "deletedChanges"),
            AddedFiles = ReadInt32(value, "addedFiles"),
            UpdatedFiles = ReadInt32(value, "updatedFiles"),
            RemovedFiles = ReadInt32(value, "removedFiles"),
            Outputs = ReadStringArray(value, "outputs"),
        };
    }

    public static QuireChange ToChange(JsonElement? element)
    {
        JsonElement value = element ?? default;
        return new QuireChange
        {
            Package = ReadString(value, "package") ?? "",
            Path = ReadString(value, "path") ?? "",
            Kind = ParseChangeKind(ReadString(value, "kind")),
        };
    }

    public static QuireKey ToKey(JsonElement? element)
    {
        JsonElement value = element ?? default;
        return new QuireKey
        {
            Name = ReadString(value, "name") ?? "",
            Fingerprint = ReadString(value, "fingerprint") ?? "",
            ClientKey = ReadString(value, "clientKey") ?? "",
        };
    }

    /// <summary>读取结果对象中的字符串字段。</summary>
    public static string? ReadResultString(JsonElement? result, string propertyName) =>
        result is { } value ? ReadString(value, propertyName) : null;

    public static TagStatus ParseStatus(string? value) => value?.ToLowerInvariant() switch
    {
        "baseline" => TagStatus.Baseline,
        "committed" => TagStatus.Committed,
        "candidate" => TagStatus.Candidate,
        "removed" => TagStatus.Removed,
        _ => TagStatus.Candidate,
    };

    private static ChangeKind ParseChangeKind(string? value) => value?.ToLowerInvariant() switch
    {
        "added" => ChangeKind.Added,
        "updated" => ChangeKind.Updated,
        "deleted" => ChangeKind.Deleted,
        _ => ChangeKind.Updated,
    };

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int ReadInt32(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out int result)
            ? result
            : 0;

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out JsonElement value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var items = new List<string>(value.GetArrayLength());
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } text)
            {
                items.Add(text);
            }
        }

        return items;
    }
}
