using System.Windows.Input;
using Avalonia.Media;

namespace QuireStudio.Presentation.ViewModels.Components;

/// <summary>「最近工作区」列表中的一项。</summary>
public sealed class RecentWorkspaceViewModel
{
    public RecentWorkspaceViewModel(string path, bool isCurrent, ICommand activateCommand)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(activateCommand);

        Path = path;
        IsCurrent = isCurrent;
        ActivateCommand = activateCommand;
    }

    public string Path { get; }

    public bool IsCurrent { get; }

    /// <summary>目录名，用于紧凑展示。</summary>
    public string Name
    {
        get
        {
            string trimmed = Path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            string name = System.IO.Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(name) ? Path : name;
        }
    }

    public IBrush Accent => new SolidColorBrush(Palette.AccentFor(Path));

    public string Initial => string.IsNullOrEmpty(Name) ? "W" : Name[..1].ToUpperInvariant();

    public ICommand ActivateCommand { get; }
}
