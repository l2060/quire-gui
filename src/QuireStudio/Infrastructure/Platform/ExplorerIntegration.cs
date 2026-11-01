using Microsoft.Win32;

namespace QuireStudio.Infrastructure.Platform;

/// <summary>
/// Windows 资源管理器右键菜单集成：在文件夹（含文件夹空白处与驱动器）上提供
/// 「在 Quire Studio 中打开」，把该目录作为工作区启动。
///
/// <para>
/// 只写入 <c>HKCU\Software\Classes</c>，当前用户生效、无需管理员权限，也不会改动系统级配置。
/// 若当前进程令牌受限（例如被沙箱限制）而无法写注册表，会导出一份 .reg 文件供用户双击导入。
/// 其他平台上所有操作都是空操作。
/// </para>
/// </summary>
public static class ExplorerIntegration
{
    private const string MenuKeyName = "QuireStudio";
    private const string MenuText = "在 Quire Studio 中打开";
    private const string FallbackFileName = "quire-studio-shell.reg";

    private static readonly string[] ShellScopes =
    [
        @"Software\Classes\Directory\shell",
        @"Software\Classes\Directory\Background\shell",
        @"Software\Classes\Drive\shell",
    ];

    /// <summary>当前平台是否支持资源管理器集成。</summary>
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>右键菜单是否已经注册。</summary>
    public static bool IsInstalled
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            try
            {
                using RegistryKey? command = Registry.CurrentUser.OpenSubKey(
                    $@"{ShellScopes[0]}\{MenuKeyName}\command");
                return command?.GetValue(null) is string value &&
                       value.Contains(ExecutablePath, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException)
            {
                return false;
            }
        }
    }

    /// <summary>注册右键菜单；成功返回 <see langword="null"/>，失败返回可读说明。</summary>
    public static string? Install()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "资源管理器集成仅在 Windows 上可用。";
        }

        string executable = ExecutablePath;

        try
        {
            foreach (string scope in ShellScopes)
            {
                string menuPath = $@"{scope}\{MenuKeyName}";
                string argument = scope.Contains(@"\Background\", StringComparison.OrdinalIgnoreCase)
                    ? "%V"
                    : "%1";

                using RegistryKey menu = Registry.CurrentUser.CreateSubKey(menuPath, writable: true);
                menu.SetValue(null, MenuText);
                menu.SetValue("Icon", $"\"{executable}\",0");

                using RegistryKey command = Registry.CurrentUser.CreateSubKey(
                    $@"{menuPath}\command",
                    writable: true);
                command.SetValue(null, $"\"{executable}\" --workspace \"{argument}\"");
            }

            return null;
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // 受限令牌常见于沙箱或策略环境：导出注册表文件，让用户用当前账户双击导入。
            string? fallback = TryWriteRegistryFile(executable);
            return fallback is null
                ? $"无法写入注册表：{exception.Message}"
                : $"当前会话无权写注册表（{exception.Message}）。已导出注册表文件：{fallback}，双击导入后即可使用右键菜单。";
        }
    }

    /// <summary>移除右键菜单；成功返回 <see langword="null"/>。</summary>
    public static string? Uninstall()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "资源管理器集成仅在 Windows 上可用。";
        }

        try
        {
            foreach (string scope in ShellScopes)
            {
                Registry.CurrentUser.DeleteSubKeyTree($@"{scope}\{MenuKeyName}", throwOnMissingSubKey: false);
            }

            return null;
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return $"无法修改注册表：{exception.Message}";
        }
    }

    /// <summary>把右键菜单写成 .reg 文件，返回文件路径；失败返回 <see langword="null"/>。</summary>
    public static string? TryWriteRegistryFile(string? executable = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        string exe = executable ?? ExecutablePath;

        var builder = new System.Text.StringBuilder();
        builder.AppendLine("Windows Registry Editor Version 5.00");
        builder.AppendLine();

        foreach (string scope in ShellScopes)
        {
            string menuKey = $@"HKEY_CURRENT_USER\{scope}\{MenuKeyName}";
            string argument = scope.Contains(@"\Background\", StringComparison.OrdinalIgnoreCase)
                ? "%V"
                : "%1";

            builder.AppendLine($"[{menuKey}]");
            builder.AppendLine($"@=\"{MenuText}\"");
            builder.AppendLine($"\"Icon\"=\"\\\"{Escape(exe)}\\\",0\"");
            builder.AppendLine();
            builder.AppendLine($"[{menuKey}\\command]");
            builder.AppendLine($"@=\"\\\"{Escape(exe)}\\\" --workspace \\\"{argument}\\\"\"");
            builder.AppendLine();
        }

        try
        {
            AppPaths.EnsureCreated();
            string path = Path.Combine(AppPaths.Root, FallbackFileName);

            // .reg 使用 UTF-16 LE + BOM，regedit 才能正确识别中文菜单名。
            File.WriteAllText(path, builder.ToString(), System.Text.Encoding.Unicode);
            return path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\");

    private static string ExecutablePath =>
        Environment.ProcessPath ?? AppContext.BaseDirectory;
}
