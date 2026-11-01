using Avalonia.Media;
using QuireStudio.Domain;

namespace QuireStudio.Presentation.ViewModels.Components;

/// <summary>一条资源变化记录。</summary>
public sealed class ChangeRowViewModel
{
    public ChangeRowViewModel(QuireChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        Change = change;
    }

    public QuireChange Change { get; }

    public string Package => Change.Package;

    public string Path => Change.Path;

    public string FileName => Change.FileName;

    public ChangeKind Kind => Change.Kind;

    public string Badge => Change.Badge.ToString();

    public string KindText => StatusStyle.Text(Change.Kind);

    public bool IsAdded => Change.Kind == ChangeKind.Added;

    public bool IsUpdated => Change.Kind == ChangeKind.Updated;

    public bool IsDeleted => Change.Kind == ChangeKind.Deleted;

    public IBrush BadgeForeground => StatusStyle.Foreground(Change.Kind);

    public IBrush BadgeBackground => StatusStyle.Background(Change.Kind);
}
