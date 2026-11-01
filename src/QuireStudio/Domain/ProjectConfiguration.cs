namespace QuireStudio.Domain;

/// <summary>项目配置中的一个源目录（<c>packages[].sources[]</c>）。</summary>
public sealed record SourceSpec
{
    /// <summary>YAML 中书写的目录。相对路径以配置所在目录或 <c>-root</c> 为基准。</summary>
    public required string Directory { get; init; }

    /// <summary>写入包内的逻辑路径前缀。</summary>
    public string Prefix { get; init; } = "";

    public IReadOnlyList<string> Include { get; init; } = [];

    public IReadOnlyList<string> Exclude { get; init; } = [];
}

/// <summary>项目配置中的一个资源包（<c>packages[]</c>）。</summary>
public sealed record PackageSpec
{
    public required string Name { get; init; }

    public string? Compression { get; init; }

    public string? CompressionPolicy { get; init; }

    public IReadOnlyList<SourceSpec> Sources { get; init; } = [];
}

/// <summary>
/// <c>quire.project.yml</c> 的只读投影。仅包含 GUI 展示与校验需要的字段，
/// 未知字段会被安全忽略，因此可以读取更新版本的 CLI 生成的配置。
/// </summary>
public sealed record ProjectConfiguration
{
    private const string NoKeyValue = "none";

    public required string FilePath { get; init; }

    public string ProjectName { get; init; } = "";

    /// <summary>输出目录模板，支持 <c>{tag}</c> 等变量。</summary>
    public string? Output { get; init; }

    public string? Suffix { get; init; }

    public string? Compression { get; init; }

    public string? CompressionPolicy { get; init; }

    /// <summary>引用的本机全局密钥名；<c>none</c> 或空表示不加密、不签名。</summary>
    public string? SecretKey { get; init; }

    public int? Jobs { get; init; }

    public int? BlockSize { get; init; }

    public IReadOnlyList<PackageSpec> Packages { get; init; } = [];

    /// <summary>配置文件所在目录，也是相对输出目录与状态目录的基准。</summary>
    public string Directory => Path.GetDirectoryName(FilePath) ?? ".";

    /// <summary>显示用项目名，配置缺少 <c>project</c> 时回退到配置文件名。</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(ProjectName)
        ? Path.GetFileNameWithoutExtension(FilePath)
        : ProjectName;

    public bool UsesEncryption => !IsDisabledKey(SecretKey);

    public int PackageCount => Packages.Count;

    public int SourceCount => Packages.Sum(package => package.Sources.Count);

    public string CompressionDescription => string.IsNullOrWhiteSpace(Compression) ? "lz4" : Compression;

    public string OutputDescription => string.IsNullOrWhiteSpace(Output) ? "./dist" : Output;

    public string SecretKeyDescription => IsDisabledKey(SecretKey) ? "未启用" : SecretKey!;

    /// <summary>
    /// 解析资源目录的绝对位置，语义与 CLI 的 <c>-root</c> 一致：
    /// 相对目录以 <paramref name="resourceRoot"/> 为基准（为空时以配置目录为基准），绝对目录保持不变。
    /// </summary>
    public IReadOnlyList<string> ResolveSourceDirectories(string? resourceRoot)
    {
        string baseDirectory = string.IsNullOrWhiteSpace(resourceRoot)
            ? Directory
            : resourceRoot;

        var directories = new List<string>();
        foreach (PackageSpec package in Packages)
        {
            foreach (SourceSpec source in package.Sources)
            {
                string? resolved = ResolveDirectory(source.Directory, baseDirectory);
                if (resolved is not null && !directories.Contains(resolved, StringComparer.OrdinalIgnoreCase))
                {
                    directories.Add(resolved);
                }
            }
        }

        return directories;
    }

    /// <summary>判断给定密钥名是否表示“关闭加密”。</summary>
    public static bool IsDisabledKey(string? key) =>
        string.IsNullOrWhiteSpace(key) || key.Equals(NoKeyValue, StringComparison.OrdinalIgnoreCase);

    private static string? ResolveDirectory(string? directory, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        try
        {
            return Path.IsPathRooted(directory)
                ? Path.GetFullPath(directory)
                : Path.GetFullPath(Path.Combine(baseDirectory, directory));
        }
        catch (ArgumentException)
        {
            // 非法路径不参与展示，交由 CLI 在打包时报出准确错误。
            return null;
        }
    }
}
