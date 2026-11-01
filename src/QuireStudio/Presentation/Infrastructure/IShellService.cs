namespace QuireStudio.Presentation.Infrastructure;

/// <summary>文件选择请求。</summary>
public sealed record FilePickerRequest(
    string Title,
    string? StartDirectory = null,
    string? FilterName = null,
    IReadOnlyList<string>? Patterns = null);

/// <summary>「添加命名密钥」对话框收集到的信息。</summary>
public sealed record NewKeyRequest(string Name, string Password);

/// <summary>「放弃候选」对话框的确认结果。</summary>
/// <param name="DeleteOutputs">是否连同打包输出一起删除。</param>
public sealed record DiscardCandidateRequest(bool DeleteOutputs);

/// <summary>
/// 平台能力封装：文件选择、剪贴板、文件管理器与模态对话框。
/// 放在接口后面，ViewModel 因此不依赖具体窗口实现，也便于测试时替换。
/// </summary>
public interface IShellService
{
    Task<string?> PickFileAsync(FilePickerRequest request);

    Task<string?> PickSaveFileAsync(FilePickerRequest request, string suggestedFileName);

    Task<string?> PickFolderAsync(string title, string? startDirectory = null);

    Task CopyTextAsync(string text);

    /// <summary>在系统文件管理器中打开目录或选中文件；失败返回 <see langword="false"/>。</summary>
    bool TryOpenInFileManager(string path);

    /// <summary>
    /// 在系统文件管理器中定位并选中某个文件（Windows 上是"在资源管理器中显示"）；
    /// 失败返回 <see langword="false"/>。
    /// </summary>
    bool TryRevealInFileManager(string filePath);

    /// <summary>用系统默认浏览器打开链接；失败返回 <see langword="false"/>。</summary>
    bool TryOpenUrl(string url);

    /// <summary>弹出「添加命名密钥」对话框；取消时返回 <see langword="null"/>。</summary>
    Task<NewKeyRequest?> ShowNewKeyDialogAsync(IReadOnlyList<string> existingNames);

    /// <summary>弹出「放弃候选」确认对话框；取消时返回 <see langword="null"/>。</summary>
    Task<DiscardCandidateRequest?> ShowDiscardCandidateDialogAsync(string tagName, int outputCount);
}

/// <summary>不执行任何操作的实现，用于窗口尚未挂载的阶段。</summary>
public sealed class NullShellService : IShellService
{
    public static NullShellService Instance { get; } = new();

    public Task<string?> PickFileAsync(FilePickerRequest request) => Task.FromResult<string?>(null);

    public Task<string?> PickSaveFileAsync(FilePickerRequest request, string suggestedFileName) =>
        Task.FromResult<string?>(null);

    public Task<string?> PickFolderAsync(string title, string? startDirectory = null) => Task.FromResult<string?>(null);

    public Task CopyTextAsync(string text) => Task.CompletedTask;

    public bool TryOpenInFileManager(string path) => false;

    public bool TryRevealInFileManager(string filePath) => false;

    public bool TryOpenUrl(string url) => false;

    public Task<NewKeyRequest?> ShowNewKeyDialogAsync(IReadOnlyList<string> existingNames) =>
        Task.FromResult<NewKeyRequest?>(null);

    public Task<DiscardCandidateRequest?> ShowDiscardCandidateDialogAsync(string tagName, int outputCount) =>
        Task.FromResult<DiscardCandidateRequest?>(null);
}
