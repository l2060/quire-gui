using CommunityToolkit.Mvvm.ComponentModel;

namespace QuireStudio.Presentation.ViewModels;

/// <summary>页面 ViewModel 基类。页面在被导航到时才加载数据。</summary>
public abstract class PageViewModel : ObservableObject
{
    protected PageViewModel(IWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        Workspace = workspace;
    }

    protected IWorkspace Workspace { get; }

    /// <summary>页面标识，与导航项一一对应。</summary>
    public abstract string Key { get; }

    /// <summary>页面进入前台时调用；默认不做事。</summary>
    public virtual Task ActivateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>强制刷新页面数据。默认复用 <see cref="ActivateAsync"/>。</summary>
    public virtual Task RefreshAsync(CancellationToken cancellationToken = default) =>
        ActivateAsync(cancellationToken);

    /// <summary>项目切换后调用，用于清空上一项目的数据。</summary>
    public virtual void OnProjectChanged()
    {
    }
}
