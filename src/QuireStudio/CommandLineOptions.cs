namespace QuireStudio;

/// <summary>启动参数。</summary>
internal sealed record CommandLineOptions
{
    public string? Page { get; init; }

    public string? Workspace { get; init; }

    /// <summary>应用数据目录覆盖（等价于设置 QUIRESTUDIO_HOME 环境变量）。</summary>
    public string? Home { get; init; }

    public bool RegisterShell { get; init; }

    public bool UnregisterShell { get; init; }

    /// <summary>
    /// 解析参数，支持 <c>--name value</c> 与 <c>--name=value</c> 两种写法。
    /// 未知参数会被忽略，交给 Avalonia 处理。
    /// </summary>
    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? page = null;
        string? workspace = null;
        string? home = null;
        bool register = false;
        bool unregister = false;

        for (int index = 0; index < args.Count; index++)
        {
            string argument = args[index];

            if (TryReadValue(args, ref index, argument, "--page", "-p", out string? pageValue))
            {
                page = pageValue;
                continue;
            }

            if (TryReadValue(args, ref index, argument, "--workspace", "-w", out string? workspaceValue))
            {
                workspace = workspaceValue;
                continue;
            }

            if (TryReadValue(args, ref index, argument, "--home", "-h", out string? homeValue))
            {
                home = homeValue;
                continue;
            }

            if (string.Equals(argument, "--register-shell", StringComparison.OrdinalIgnoreCase))
            {
                register = true;
            }
            else if (string.Equals(argument, "--unregister-shell", StringComparison.OrdinalIgnoreCase))
            {
                unregister = true;
            }
        }

        return new CommandLineOptions
        {
            Page = page,
            Workspace = workspace,
            Home = home,
            RegisterShell = register,
            UnregisterShell = unregister,
        };
    }

    private static bool TryReadValue(
        IReadOnlyList<string> args,
        ref int index,
        string argument,
        string longName,
        string shortName,
        out string? value)
    {
        value = null;

        if (string.Equals(argument, longName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(argument, shortName, StringComparison.OrdinalIgnoreCase))
        {
            if (index + 1 < args.Count)
            {
                value = args[++index];
            }

            return true;
        }

        foreach (string prefix in (string[])[$"{longName}=", $"{shortName}="])
        {
            if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                value = argument[prefix.Length..];
                return true;
            }
        }

        return false;
    }
}
