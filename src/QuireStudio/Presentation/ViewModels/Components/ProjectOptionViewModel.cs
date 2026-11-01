using System.Windows.Input;
using Avalonia.Media;
using QuireStudio.Domain;

namespace QuireStudio.Presentation.ViewModels.Components;

/// <summary>项目切换列表中的一项。</summary>
public sealed class ProjectOptionViewModel
{
    public ProjectOptionViewModel(ProjectRecord record, bool isActive, ICommand activateCommand)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(activateCommand);

        Record = record;
        IsActive = isActive;
        ActivateCommand = activateCommand;
    }

    public ProjectRecord Record { get; }

    public string Name => string.IsNullOrWhiteSpace(Record.Name) ? Record.ConfigFileName : Record.Name;

    public string ConfigFileName => Record.ConfigFileName;

    public bool IsActive { get; }

    public bool IsManaged => Record.IsManaged;

    public bool ConfigExists => Record.ConfigExists;

    public IBrush Accent => new SolidColorBrush(Palette.AccentFor(Name));

    public string Initial => string.IsNullOrEmpty(Name) ? "P" : Name[..1].ToUpperInvariant();

    public ICommand ActivateCommand { get; }
}
