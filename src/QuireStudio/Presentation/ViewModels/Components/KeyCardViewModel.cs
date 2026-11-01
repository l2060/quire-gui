using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuireStudio.Domain;

namespace QuireStudio.Presentation.ViewModels.Components;

/// <summary>密钥库中的一张卡片。</summary>
public sealed partial class KeyCardViewModel : ObservableObject
{
    public KeyCardViewModel(QuireKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        Key = key;
    }

    public QuireKey Key { get; }

    public string Name => Key.Name;

    public string Fingerprint => Key.Fingerprint;

    public string ShortFingerprint => Key.ShortFingerprint;

    public string Initial => string.IsNullOrEmpty(Key.Name) ? "K" : Key.Name[..1].ToUpperInvariant();

    public IBrush Accent => new SolidColorBrush(Palette.AccentFor(Key.Name));

    public string KeyLengthText => $"{Key.ClientKey.Length} 字符";

    /// <summary>客户端密钥默认遮蔽，避免无意间暴露在屏幕上。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayKey))]
    private bool _isRevealed;

    /// <summary>删除需要二次点击确认，避免误操作。</summary>
    [ObservableProperty]
    private bool _isConfirmingRemoval;

    public string DisplayKey => IsRevealed ? Key.ClientKey : Key.MaskedClientKey;

    [RelayCommand]
    private void ToggleReveal() => IsRevealed = !IsRevealed;

    [RelayCommand]
    private void RequestRemove() => IsConfirmingRemoval = true;

    [RelayCommand]
    private void CancelRemove() => IsConfirmingRemoval = false;
}
