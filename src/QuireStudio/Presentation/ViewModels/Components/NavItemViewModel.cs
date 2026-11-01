using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace QuireStudio.Presentation.ViewModels.Components;

/// <summary>侧边栏导航项。</summary>
public sealed partial class NavItemViewModel : ObservableObject
{
    public NavItemViewModel(string key, string title, Geometry icon, PageViewModel page)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(icon);
        ArgumentNullException.ThrowIfNull(page);

        Key = key;
        Title = title;
        Icon = icon;
        Page = page;
    }

    public string Key { get; }

    public string Title { get; }

    public Geometry Icon { get; }

    public PageViewModel Page { get; }

    /// <summary>可选的角标文字，例如候选数量。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBadge))]
    private string? _badge;

    public bool HasBadge => !string.IsNullOrWhiteSpace(Badge);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconBrush))]
    [NotifyPropertyChangedFor(nameof(LabelBrush))]
    [NotifyPropertyChangedFor(nameof(RowBackground))]
    private bool _isActive;

    public IBrush IconBrush => IsActive ? Palette.Accent : Palette.TextTertiary;

    public IBrush LabelBrush => IsActive ? Palette.Text : Palette.TextSecondary;

    public IBrush RowBackground => IsActive ? Palette.RowActive : Palette.Transparent;
}
