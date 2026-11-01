using System.Globalization;
using QuireStudio.Domain;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace QuireStudio.Infrastructure.Configuration;

/// <summary>配置读取结果。</summary>
public sealed record ProjectConfigurationLoadResult
{
    public bool Succeeded { get; init; }

    public ProjectConfiguration? Configuration { get; init; }

    public string? Error { get; init; }

    public static ProjectConfigurationLoadResult Success(ProjectConfiguration configuration) =>
        new() { Succeeded = true, Configuration = configuration };

    public static ProjectConfigurationLoadResult Failure(string error) =>
        new() { Succeeded = false, Error = error };
}

/// <summary>
/// 读取 <c>quire.project.yml</c>。只解析界面需要展示与校验的字段，
/// 其余键（包括未来新增的键）会被忽略，因此不会因 CLI 演进导致读取失败。
/// </summary>
public sealed class ProjectConfigurationReader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder().Build();

    /// <summary>读取配置文件；文件缺失或格式错误时返回失败结果。</summary>
    public ProjectConfigurationLoadResult Read(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return ProjectConfigurationLoadResult.Failure("配置文件路径为空。");
        }

        if (!File.Exists(filePath))
        {
            return ProjectConfigurationLoadResult.Failure($"配置文件不存在：{filePath}");
        }

        string yaml;
        try
        {
            yaml = File.ReadAllText(filePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ProjectConfigurationLoadResult.Failure($"无法读取配置文件：{exception.Message}");
        }

        return Parse(filePath, yaml);
    }

    /// <summary>解析 YAML 文本。</summary>
    public ProjectConfigurationLoadResult Parse(string filePath, string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return ProjectConfigurationLoadResult.Failure("配置文件为空。");
        }

        object? root;
        try
        {
            root = Deserializer.Deserialize<object>(yaml);
        }
        catch (YamlException exception)
        {
            return ProjectConfigurationLoadResult.Failure($"YAML 解析失败：{exception.Message}");
        }

        if (AsMap(root) is not { } map)
        {
            return ProjectConfigurationLoadResult.Failure("配置根节点必须是 YAML 映射。");
        }

        IDictionary<string, object?> build = AsMap(GetValue(map, "build")) ?? new Dictionary<string, object?>();

        return ProjectConfigurationLoadResult.Success(new ProjectConfiguration
        {
            FilePath = ToFullPath(filePath),
            ProjectName = GetScalar(map, "project") ?? "",
            Output = GetScalar(map, "output"),
            Suffix = GetScalar(map, "suffix"),
            Compression = GetScalar(map, "compression"),
            CompressionPolicy = GetScalar(map, "compression-policy"),
            SecretKey = GetScalar(map, "secret-key"),
            Jobs = GetInt32(build, "jobs"),
            BlockSize = GetInt32(build, "block-size"),
            Packages = ReadPackages(GetValue(map, "packages")),
        });
    }

    private static IReadOnlyList<PackageSpec> ReadPackages(object? node)
    {
        var packages = new List<PackageSpec>();
        foreach (object? packageNode in AsSequence(node))
        {
            if (AsMap(packageNode) is not { } packageMap)
            {
                continue;
            }

            var sources = new List<SourceSpec>();
            foreach (object? sourceNode in AsSequence(GetValue(packageMap, "sources")))
            {
                if (AsMap(sourceNode) is not { } sourceMap)
                {
                    continue;
                }

                sources.Add(new SourceSpec
                {
                    Directory = GetScalar(sourceMap, "directory") ?? "",
                    Prefix = GetScalar(sourceMap, "prefix") ?? "",
                    Include = AsStringList(GetValue(sourceMap, "include")),
                    Exclude = AsStringList(GetValue(sourceMap, "exclude")),
                });
            }

            packages.Add(new PackageSpec
            {
                Name = GetScalar(packageMap, "name") ?? $"package-{packages.Count + 1}",
                Compression = GetScalar(packageMap, "compression"),
                CompressionPolicy = GetScalar(packageMap, "compression-policy"),
                Sources = sources,
            });
        }

        return packages;
    }

    private static string ToFullPath(string filePath)
    {
        try
        {
            return Path.GetFullPath(filePath);
        }
        catch (ArgumentException)
        {
            return filePath;
        }
    }

    private static object? GetValue(IDictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out object? value) ? value : null;

    private static IDictionary<string, object?>? AsMap(object? value)
    {
        if (value is not IDictionary<object, object> map)
        {
            return null;
        }

        var normalized = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (KeyValuePair<object, object> pair in map)
        {
            if (pair.Key?.ToString() is { Length: > 0 } key)
            {
                normalized[key] = pair.Value;
            }
        }

        return normalized;
    }

    private static IReadOnlyList<object?> AsSequence(object? value) => value switch
    {
        null => [],
        IList<object> list => list.Cast<object?>().ToArray(),
        _ => [value],
    };

    private static IReadOnlyList<string> AsStringList(object? value) =>
        AsSequence(value)
            .Select(item => item as string)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToArray();

    private static string? GetScalar(IDictionary<string, object?> map, string key)
    {
        if (!map.TryGetValue(key, out object? value))
        {
            return null;
        }

        return value switch
        {
            null => null,
            string text => text,
            bool flag => flag ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => null,
        };
    }

    private static int? GetInt32(IDictionary<string, object?> map, string key) =>
        int.TryParse(GetScalar(map, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : null;
}
