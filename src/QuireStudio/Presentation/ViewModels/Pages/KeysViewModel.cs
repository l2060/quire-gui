using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuireStudio.Domain;
using QuireStudio.Infrastructure.Cli;
using QuireStudio.Presentation.Infrastructure;
using QuireStudio.Presentation.ViewModels.Components;

namespace QuireStudio.Presentation.ViewModels.Pages;

/// <summary>
/// 密钥库页。密钥是「当前用户的全局资源」，与项目无关，
/// 项目只通过 YAML 的 <c>secret-key</c> 引用名称。
/// </summary>
public sealed partial class KeysViewModel : BusyPageViewModel
{
    public KeysViewModel(IWorkspace workspace)
        : base(workspace)
    {
    }

    public override string Key => "keys";

    public ObservableCollection<KeyCardViewModel> Keys { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddKey))]
    private bool _isAdding;

    public bool HasKeys => Keys.Count > 0;

    public string KeyCountText => Keys.Count == 0 ? "暂无密钥" : $"{Keys.Count} 个密钥";

    public bool CanAddKey => !IsAdding;

    public ActivityLog Log => Workspace.Log;

    /// <summary>读取本机全部命名密钥。</summary>
    [RelayCommand]
    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        ClearError();

        try
        {
            CliOutcome<IReadOnlyList<QuireKey>> outcome = await Workspace.Cli
                .GetKeysAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(true);

            if (!outcome.Succeeded)
            {
                Keys.Clear();
                OnPropertyChanged(nameof(HasKeys));
                OnPropertyChanged(nameof(KeyCountText));
                ReportFailure(outcome.Error, "读取密钥失败。");
                return;
            }

            Keys.Clear();
            foreach (QuireKey key in outcome.Value!.OrderBy(key => key.Name, StringComparer.OrdinalIgnoreCase))
            {
                Keys.Add(new KeyCardViewModel(key));
            }

            OnPropertyChanged(nameof(HasKeys));
            OnPropertyChanged(nameof(KeyCountText));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public override Task ActivateAsync(CancellationToken cancellationToken = default) =>
        RefreshAsync(cancellationToken);

    public override void OnProjectChanged()
    {
        // 密钥与项目无关，切换项目不需要重新读取。
    }

    /// <summary>通过对话框添加命名密钥。</summary>
    [RelayCommand(CanExecute = nameof(CanAddKey))]
    private async Task AddKeyAsync()
    {
        NewKeyRequest? request = await Workspace.Shell
            .ShowNewKeyDialogAsync(Keys.Select(key => key.Name).ToArray())
            .ConfigureAwait(true);

        if (request is null)
        {
            return;
        }

        IsAdding = true;
        ClearError();

        try
        {
            Workspace.Log.Info($"添加密钥：{request.Name}");

            CliOutcome<QuireKey> outcome = await Workspace.Cli
                .AddKeyAsync(request.Name, request.Password)
                .ConfigureAwait(true);

            if (!outcome.Succeeded)
            {
                ReportFailure(outcome.Error, "添加密钥失败。");
                return;
            }

            Workspace.Log.Success($"密钥 {request.Name} 已添加，指纹 {outcome.Value!.ShortFingerprint}");
            await RefreshAsync().ConfigureAwait(true);
        }
        finally
        {
            IsAdding = false;
        }
    }

    /// <summary>复制客户端密钥字符串。</summary>
    [RelayCommand]
    private async Task CopyKeyAsync(KeyCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        await Workspace.Shell.CopyTextAsync(card.Key.ClientKey).ConfigureAwait(true);
        Workspace.Log.Success($"已复制 {card.Name} 的客户端密钥（{card.KeyLengthText}）。");
    }

    /// <summary>导出二进制客户端密钥文件。</summary>
    [RelayCommand]
    private async Task ExportKeyAsync(KeyCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        string? destination = await Workspace.Shell
            .PickSaveFileAsync(
                new FilePickerRequest(
                    "导出客户端密钥",
                    Workspace.Project?.Directory,
                    "客户端密钥",
                    ["key"]),
                $"{card.Name}.key")
            .ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(destination))
        {
            return;
        }

        CliOutcome<string> outcome = await Workspace.Cli
            .ExportKeyAsync(card.Name, destination)
            .ConfigureAwait(true);

        if (!outcome.Succeeded)
        {
            ReportFailure(outcome.Error, "导出密钥失败。");
            return;
        }

        Workspace.Log.Success($"客户端密钥已导出：{outcome.Value}");
    }

    /// <summary>删除本机密钥条目。</summary>
    [RelayCommand]
    private async Task RemoveKeyAsync(KeyCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        CliOutcome<string> outcome = await Workspace.Cli
            .RemoveKeyAsync(card.Name)
            .ConfigureAwait(true);

        card.IsConfirmingRemoval = false;

        if (!outcome.Succeeded)
        {
            ReportFailure(outcome.Error, "删除密钥失败。");
            return;
        }

        Workspace.Log.Warning($"密钥 {card.Name} 已删除；已导出的客户端密钥不受影响。");
        await RefreshAsync().ConfigureAwait(true);
    }
}
