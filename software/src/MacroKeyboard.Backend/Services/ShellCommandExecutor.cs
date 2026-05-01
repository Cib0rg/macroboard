using System.Diagnostics;
using System.Runtime.InteropServices;
using MacroKeyboard.Core.Models;
using Microsoft.Extensions.Logging;

namespace MacroKeyboard.Backend.Services;

/// <summary>
/// Результат выполнения shell-команды
/// </summary>
public class ShellResult
{
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Интерфейс для выполнения shell-команд
/// </summary>
public interface IShellCommandExecutor
{
    Task<ShellResult> ExecuteAsync(ShellAction action, CancellationToken cancellationToken = default);
    Task<ShellResult> ExecuteAsync(string command, string? workingDirectory = null, bool waitForExit = true, int timeoutMs = 30000, CancellationToken cancellationToken = default);
}

/// <summary>
/// Сервис для выполнения shell-команд на PC
/// </summary>
public class ShellCommandExecutor : IShellCommandExecutor
{
    private readonly ILogger<ShellCommandExecutor> _logger;
    private readonly SemaphoreSlim _semaphore;
    private const int MaxConcurrentCommands = 3;

    public ShellCommandExecutor(ILogger<ShellCommandExecutor> logger)
    {
        _logger = logger;
        _semaphore = new SemaphoreSlim(MaxConcurrentCommands, MaxConcurrentCommands);
    }

    /// <summary>
    /// Выполнить shell-команду из ShellAction
    /// </summary>
    public async Task<ShellResult> ExecuteAsync(ShellAction action, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(
            action.Command,
            action.WorkingDirectory,
            action.WaitForExit,
            action.TimeoutMs,
            cancellationToken);
    }

    /// <summary>
    /// Выполнить shell-команду
    /// </summary>
    public async Task<ShellResult> ExecuteAsync(
        string command,
        string? workingDirectory = null,
        bool waitForExit = true,
        int timeoutMs = 30000,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return new ShellResult
            {
                Success = false,
                Error = "Command is empty"
            };
        }

        _logger.LogInformation("Executing shell command: {Command}", command);
        var stopwatch = Stopwatch.StartNew();

        // Ограничиваем количество одновременных команд
        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = GetShellExecutable(),
                Arguments = GetShellArguments(command),
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            
            var outputBuilder = new System.Text.StringBuilder();
            var errorBuilder = new System.Text.StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    outputBuilder.AppendLine(e.Data);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    errorBuilder.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!waitForExit)
            {
                _logger.LogDebug("Command started without waiting: {Command}", command);
                stopwatch.Stop();
                return new ShellResult
                {
                    Success = true,
                    Duration = stopwatch.Elapsed
                };
            }

            // Ожидаем завершения с таймаутом
            var effectiveTimeout = timeoutMs > 0 ? timeoutMs : Timeout.Infinite;
            
            using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                _logger.LogWarning("Command timed out after {Timeout}ms: {Command}", timeoutMs, command);
                
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to kill timed out process");
                }

                stopwatch.Stop();
                return new ShellResult
                {
                    Success = false,
                    Error = $"Command timed out after {timeoutMs}ms",
                    Output = outputBuilder.ToString(),
                    Duration = stopwatch.Elapsed
                };
            }

            stopwatch.Stop();

            var result = new ShellResult
            {
                Success = process.ExitCode == 0,
                ExitCode = process.ExitCode,
                Output = outputBuilder.ToString().TrimEnd(),
                Error = errorBuilder.ToString().TrimEnd(),
                Duration = stopwatch.Elapsed
            };

            if (result.Success)
            {
                _logger.LogInformation("Command completed successfully in {Duration}ms: {Command}",
                    result.Duration.TotalMilliseconds, command);
            }
            else
            {
                _logger.LogWarning("Command failed with exit code {ExitCode}: {Command}. Error: {Error}",
                    result.ExitCode, command, result.Error);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Shell command execution failed: {Command}", command);
            stopwatch.Stop();
            return new ShellResult
            {
                Success = false,
                Error = ex.Message,
                Duration = stopwatch.Elapsed
            };
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Получить исполняемый файл shell в зависимости от ОС
    /// </summary>
    private static string GetShellExecutable()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "cmd.exe";
        }
        
        // Linux/macOS
        return "/bin/bash";
    }

    /// <summary>
    /// Получить аргументы для shell в зависимости от ОС
    /// </summary>
    private static string GetShellArguments(string command)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return $"/c {command}";
        }
        
        // Linux/macOS - экранируем кавычки в команде
        var escapedCommand = command.Replace("\"", "\\\"");
        return $"-c \"{escapedCommand}\"";
    }
}
