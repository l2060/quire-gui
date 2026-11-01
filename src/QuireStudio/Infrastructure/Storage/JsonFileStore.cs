using System.Text.Json;

namespace QuireStudio.Infrastructure.Storage;

/// <summary>
/// 统一的 JSON 读写入口：固定序列化选项、写入采用临时文件替换，避免半截文件损坏状态。
/// </summary>
internal static class JsonFileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>读取 JSON，文件缺失或内容非法时返回 <see langword="null"/> 而不抛出。</summary>
    public static T? Read<T>(string filePath)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            string json = File.ReadAllText(filePath);
            return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<T>(json, SerializerOptions);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>原子写入 JSON：先写临时文件再替换目标。</summary>
    public static void Write<T>(string filePath, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(value);

        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = filePath + ".tmp";
        string json = JsonSerializer.Serialize(value, SerializerOptions);
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, filePath, overwrite: true);
    }
}
