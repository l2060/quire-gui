using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace QuireStudio.Infrastructure.Cli;

/// <summary>
/// 启动 <c>quire-cli</c> 子进程并解析其 NDJSON 事件流。
/// 协议要点：同时异步读取 stdout 与 stderr；只有收到 <c>completed</c> 且退出码为 0 才算成功。
/// </summary>
public sealed class CliProcessRunner
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// 执行一次 CLI 调用。<paramref name="onEvent"/> 在后台线程按输出顺序触发，
    /// 调用方需要自行切回 UI 线程。
    /// </summary>
    /// <param name="executablePath"><c>quire-cli</c> 可执行文件的绝对路径。</param>
    /// <param name="arguments">命令行参数（不含可执行文件本身）。</param>
    /// <param name="workingDirectory">子进程工作目录；为空时沿用当前进程。</param>
    /// <param name="onEvent">每解析到一个 NDJSON 事件时回调，用于驱动进度显示。</param>
    /// <param name="displayCommand">记录到日志里的命令文本；为空时由参数自动拼接。</param>
    /// <param name="cancellationToken">取消令牌；触发后立即结束子进程。</param>
    /// <param name="timeout">
    /// 可选看门狗，语义是<b>空闲超时</b>：连续这么久没有任何输出才认为 CLI 挂起并强制结束。
    /// 只要子进程还在持续输出（例如打包进度），运行多久都不会被终止。
    /// </param>
    public async Task<CliResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        Action<CliEvent>? onEvent = null,
        string? displayCommand = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);

        string commandText = displayCommand ?? CliResult.BuildCommandText(arguments);

        // 安装出来的启动器是脚本（Windows 上为 .cmd），需要交给命令解释器执行。
        bool isScript = OperatingSystem.IsWindows() &&
                        (executablePath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
                         executablePath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase));

        string fileName = isScript
            ? Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe"
            : executablePath;

        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom,
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        if (isScript)
        {
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(executablePath);
        }

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return CliResult.NotStarted("无法启动 quire-cli 进程。", arguments, commandText);
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return CliResult.NotStarted($"无法启动 quire-cli：{exception.Message}", arguments, commandText);
        }

        var items = new List<CliEvent>();
        CliEvent? terminal = null;
        string? protocolError = null;
        bool timedOut = false;

        using CancellationTokenRegistration registration = cancellationToken.Register(() => TryKill(process));

        using Timer? watchdog = timeout is { } limit && limit > TimeSpan.Zero
            ? new Timer(
                _ =>
                {
                    timedOut = true;
                    TryKill(process);
                },
                null,
                limit,
                Timeout.InfiniteTimeSpan)
            : null;

        void Touch() => watchdog?.Change(timeout!.Value, Timeout.InfiniteTimeSpan);

        Task<string> diagnosticsTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            while (await process.StandardOutput.ReadLineAsync(CancellationToken.None).ConfigureAwait(false) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                // 只要有输出就重置看门狗：长任务靠进展证明自己还活着，
                // 只有真正"没有任何输出"才算挂起。
                Touch();

                CliEvent? parsed = CliEventParser.Parse(line, out string? parseError);
                if (parsed is null)
                {
                    protocolError ??= parseError;
                    continue;
                }

                // 终态之后的事件按协议无效，直接忽略，保证只呈现一个终态。
                if (terminal is not null)
                {
                    continue;
                }

                if (parsed.Kind == CliEventKind.Item)
                {
                    items.Add(parsed);
                }

                if (parsed.IsTerminal)
                {
                    terminal = parsed;
                }

                onEvent?.Invoke(parsed);
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ObjectDisposedException)
        {
            protocolError ??= exception.Message;
        }

        string diagnostics = await ReadDiagnosticsAsync(diagnosticsTask).ConfigureAwait(false);
        int exitCode = TryGetExitCode(process);
        bool canceled = cancellationToken.IsCancellationRequested;
        bool succeeded = exitCode == 0 && terminal?.Kind == CliEventKind.Completed;

        string? errorMessage = succeeded
            ? null
            : timedOut
                ? $"quire-cli 连续 {timeout!.Value.TotalSeconds:0} 秒没有任何输出，已终止。大工程请重试或手动运行 quire-cli 排查。"
                : terminal?.ErrorMessage ?? BuildFailureMessage(canceled, exitCode, diagnostics, protocolError);

        return new CliResult
        {
            Arguments = arguments,
            CommandText = commandText,
            Succeeded = succeeded,
            ExitCode = exitCode,
            TerminalKind = terminal?.Kind ?? (canceled || timedOut ? CliEventKind.Canceled : CliEventKind.Unknown),
            Result = terminal?.Result,
            Items = items,
            ErrorCode = terminal?.ErrorCode
                        ?? (timedOut ? "timeout" : canceled ? "canceled" : protocolError is null ? null : "protocol_error"),
            ErrorMessage = errorMessage,
            Diagnostics = diagnostics,
        };
    }

    private static string BuildFailureMessage(bool canceled, int exitCode, string diagnostics, string? protocolError)
    {
        if (canceled)
        {
            return "操作已取消。";
        }

        if (!string.IsNullOrWhiteSpace(diagnostics))
        {
            return diagnostics.Trim();
        }

        return protocolError ?? $"quire-cli 未成功完成，退出码 {exitCode}。";
    }

    private static async Task<string> ReadDiagnosticsAsync(Task<string> diagnosticsTask)
    {
        try
        {
            return await diagnosticsTask.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            return "";
        }
    }

    private static int TryGetExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            // 进程已自行退出或无权终止，交由调用方根据退出码判定结果。
        }
    }
}
