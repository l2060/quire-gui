using System.Text.Json.Serialization;

namespace QuireStudio.Domain;

/// <summary>
/// GUI 统一管理的项目登记项。所有项目都记录在工作区的 <c>projects.json</c> 中，
/// 与配置文件所在地解耦：项目可以位于工作区内，也可以指向已有的仓库。
/// </summary>
public sealed record ProjectRecord
{
    /// <summary>稳定标识，用于关联 UI 状态与设置。</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("n");

    /// <summary>显示名称，默认取配置中的 <c>project</c>。</summary>
    public string Name { get; init; } = "";

    /// <summary><c>quire.project.yml</c> 的绝对路径。</summary>
    public string ConfigPath { get; init; } = "";

    /// <summary>打包时通过 <c>-root</c> 传入的资源根目录；为空表示沿用配置目录。</summary>
    public string? ResourceRoot { get; init; }

    /// <summary>项目目录是否由 GUI 在工作区中创建。</summary>
    public bool IsManaged { get; init; }

    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastOpenedUtc { get; init; }

    /// <summary>配置文件所在目录。</summary>
    [JsonIgnore]
    public string Directory => Path.GetDirectoryName(ConfigPath) ?? "";

    /// <summary>配置文件名，用于紧凑展示。</summary>
    [JsonIgnore]
    public string ConfigFileName => Path.GetFileName(ConfigPath);

    /// <summary>配置是否存在，用于在列表中标出失效项目。</summary>
    [JsonIgnore]
    public bool ConfigExists => !string.IsNullOrWhiteSpace(ConfigPath) && File.Exists(ConfigPath);
}
