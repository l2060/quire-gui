using QuireStudio.Domain;

namespace QuireStudio.Infrastructure.Storage;

/// <summary>
/// 项目登记表：把「GUI 统一管理的项目」与「配置文件所在位置」解耦。
/// 每个项目只在 <c>projects.json</c> 中出现一次，以配置文件路径做去重。
/// </summary>
public sealed class ProjectRegistry
{
    private readonly string? _filePath;
    private RegistryDocument _document;

    private ProjectRegistry(string? filePath, RegistryDocument document)
    {
        _filePath = filePath;
        _document = document;
    }

    /// <summary>创建一个尚未绑定工作区的空登记表（保存时不做任何事）。</summary>
    public static ProjectRegistry CreateEmpty() => new(null, new RegistryDocument());

    /// <summary>当前登记的项目，保持登记顺序。</summary>
    public IReadOnlyList<ProjectRecord> Projects => _document.Projects;

    public int Count => _document.Projects.Count;

    /// <summary>当前激活项目的标识。</summary>
    public string? ActiveProjectId => _document.ActiveProjectId;

    public ProjectRecord? ActiveProject =>
        _document.Projects.FirstOrDefault(project => project.Id == _document.ActiveProjectId);

    /// <summary>从磁盘载入登记表，文件缺失时返回空表。</summary>
    public static ProjectRegistry Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string path = filePath;
        RegistryDocument document = JsonFileStore.Read<RegistryDocument>(path) ?? new RegistryDocument();
        document.Projects ??= [];

        // 丢弃配置已消失的重复项，保持列表干净。
        document.Projects = document.Projects
            .Where(project => !string.IsNullOrWhiteSpace(project.ConfigPath))
            .GroupBy(project => project.ConfigPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(project => project.LastOpenedUtc).First())
            .ToList();

        if (document.Projects.All(project => project.Id != document.ActiveProjectId))
        {
            document.ActiveProjectId = document.Projects.FirstOrDefault()?.Id;
        }

        return new ProjectRegistry(path, document);
    }

    public ProjectRecord? Find(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : _document.Projects.FirstOrDefault(project => project.Id == id);

    public ProjectRecord? FindByConfigPath(string? configPath) =>
        string.IsNullOrWhiteSpace(configPath)
            ? null
            : _document.Projects.FirstOrDefault(
                project => string.Equals(project.ConfigPath, configPath, StringComparison.OrdinalIgnoreCase));

    /// <summary>登记或更新项目，按 Id 与配置文件路径去重。</summary>
    public ProjectRecord Register(ProjectRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        int index = _document.Projects.FindIndex(project =>
            project.Id == record.Id ||
            string.Equals(project.ConfigPath, record.ConfigPath, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            ProjectRecord existing = _document.Projects[index];
            ProjectRecord merged = record with
            {
                Id = existing.Id,
                CreatedUtc = existing.CreatedUtc,
                LastOpenedUtc = DateTimeOffset.UtcNow,
            };
            _document.Projects[index] = merged;
            return merged;
        }

        var created = record with { LastOpenedUtc = DateTimeOffset.UtcNow };
        _document.Projects.Add(created);
        return created;
    }

    public bool Remove(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        int removed = _document.Projects.RemoveAll(project => project.Id == id);
        if (removed == 0)
        {
            return false;
        }

        if (_document.ActiveProjectId == id)
        {
            _document.ActiveProjectId = _document.Projects.FirstOrDefault()?.Id;
        }

        return true;
    }

    public void SetActive(string? id)
    {
        _document.ActiveProjectId = Find(id)?.Id;
    }

    /// <summary>把登记表写回磁盘；失败时返回 <see langword="false"/> 而不抛出。</summary>
    public bool Save()
    {
        // 尚未选择工作区时没有登记表可写。
        if (_filePath is null)
        {
            return true;
        }

        try
        {
            JsonFileStore.Write(_filePath, _document);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            LastError = exception.Message;
            return false;
        }
    }

    /// <summary>最近一次保存失败的原因。</summary>
    public string? LastError { get; private set; }

    private sealed class RegistryDocument
    {
        public int Version { get; set; } = 1;

        public string? ActiveProjectId { get; set; }

        public List<ProjectRecord> Projects { get; set; } = [];
    }
}
