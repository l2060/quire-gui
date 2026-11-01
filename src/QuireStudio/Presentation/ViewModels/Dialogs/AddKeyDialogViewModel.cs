using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuireStudio.Presentation.Infrastructure;

namespace QuireStudio.Presentation.ViewModels.Dialogs;

/// <summary>
/// 「添加命名密钥」对话框的 ViewModel。只负责校验与收集输入，
/// 真正的 CLI 调用由密钥库页面在对话框关闭后执行。
/// </summary>
public sealed partial class AddKeyDialogViewModel : ObservableObject
{
    private const int MinimumPasswordLength = 8;

    private readonly HashSet<string> _existingNames;

    public AddKeyDialogViewModel(IEnumerable<string>? existingNames = null)
    {
        _existingNames = new HashSet<string>(
            existingNames ?? [],
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>用户确认提交。</summary>
    public event EventHandler? Submitted;

    /// <summary>用户取消。</summary>
    public event EventHandler? Cancelled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    [NotifyPropertyChangedFor(nameof(NameWarning))]
    [NotifyPropertyChangedFor(nameof(HasNameWarning))]
    private string _name = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    [NotifyPropertyChangedFor(nameof(PasswordError))]
    [NotifyPropertyChangedFor(nameof(HasPasswordError))]
    [NotifyPropertyChangedFor(nameof(ConfirmationError))]
    [NotifyPropertyChangedFor(nameof(HasConfirmationError))]
    private string _password = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    [NotifyPropertyChangedFor(nameof(ConfirmationError))]
    [NotifyPropertyChangedFor(nameof(HasConfirmationError))]
    private string _confirmation = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public string PasswordHint => $"至少 {MinimumPasswordLength} 位高熵密码；密码不会被保存，也无法找回。";

    public string? NameWarning => IsExistingName(Name) ? "已存在同名密钥，保存后会覆盖原有条目。" : null;

    public bool HasNameWarning => NameWarning is not null;

    public string? PasswordError => Password.Length is > 0 and < MinimumPasswordLength
        ? $"密码至少需要 {MinimumPasswordLength} 位。"
        : null;

    public bool HasPasswordError => PasswordError is not null;

    public string? ConfirmationError => Confirmation.Length > 0 &&
                                        !string.Equals(Password, Confirmation, StringComparison.Ordinal)
        ? "两次输入的密码不一致。"
        : null;

    public bool HasConfirmationError => ConfirmationError is not null;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool CanSubmit =>
        IsValidName(Name) &&
        Password.Length >= MinimumPasswordLength &&
        string.Equals(Password, Confirmation, StringComparison.Ordinal);

    /// <summary>生成提交给调用方的请求。</summary>
    public NewKeyRequest CreateRequest() => new(Name.Trim(), Password);

    [RelayCommand]
    private void Confirm()
    {
        ErrorMessage = null;

        if (!IsValidName(Name))
        {
            ErrorMessage = "密钥名称不能为空，且不能包含路径分隔符或文件名非法字符。";
            return;
        }

        if (Password.Length < MinimumPasswordLength)
        {
            ErrorMessage = PasswordError;
            return;
        }

        if (!string.Equals(Password, Confirmation, StringComparison.Ordinal))
        {
            ErrorMessage = ConfirmationError;
            return;
        }

        Submitted?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);

    private bool IsExistingName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && _existingNames.Contains(name.Trim());

    private static bool IsValidName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        string trimmed = name.Trim();
        if (trimmed.StartsWith('.') || trimmed.StartsWith('-'))
        {
            return false;
        }

        return trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
               trimmed.IndexOfAny(['/', '\\']) < 0;
    }
}
