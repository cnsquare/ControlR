using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using ControlR.Libraries.Api.Contracts.Constants;
using ControlR.Libraries.Shared.Services.FileSystem;
using ControlR.Libraries.Shared.Services.Processes;

namespace ControlR.Agent.Common.Services.Scripting;

internal interface IScriptExecutor
{
  Task<ScriptExecutionResultDto> Execute(RunScriptRequestDto request, CancellationToken cancellationToken);
}

internal class ScriptExecutor(
  IFileSystem fileSystem,
  IFileSystemPathProvider pathProvider,
  IProcessManager processManager,
  TimeProvider timeProvider,
  ILogger<ScriptExecutor> logger) : IScriptExecutor
{
  private const string ScriptDirectoryName = "ScriptRuns";

  private static readonly TimeSpan _outputDrainTimeout = TimeSpan.FromSeconds(5);

  private readonly IFileSystem _fileSystem = fileSystem;
  private readonly ILogger<ScriptExecutor> _logger = logger;
  private readonly IFileSystemPathProvider _pathProvider = pathProvider;
  private readonly IProcessManager _processManager = processManager;
  private readonly TimeProvider _timeProvider = timeProvider;

  public async Task<ScriptExecutionResultDto> Execute(RunScriptRequestDto request, CancellationToken cancellationToken)
  {
    var startedAt = _timeProvider.GetUtcNow();
    string? scriptPath = null;

    try
    {
      if (request.Content.Length > ScriptLimits.MaxContentLength)
      {
        return Failed(request, startedAt, "The script exceeds the maximum length.");
      }

      if (!IsShellSupported(request.Shell))
      {
        return Failed(request, startedAt, $"{request.Shell} is not available on this operating system.");
      }

      scriptPath = await WriteScriptFile(request, cancellationToken);
      return await RunProcess(request, scriptPath, startedAt, cancellationToken);
    }
    catch (Win32Exception ex)
    {
      _logger.LogWarning(ex, "Could not start {Shell} for script result {ScriptResultId}.", request.Shell, request.ScriptResultId);
      return Failed(request, startedAt, $"{request.Shell} is not installed on this device.");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error while running script result {ScriptResultId}.", request.ScriptResultId);
      return Failed(request, startedAt, "The agent could not run the script.");
    }
    finally
    {
      TryDeleteScript(scriptPath);
    }
  }

  internal static ProcessStartInfo CreateStartInfo(ScriptShell shell, string scriptPath, string workingDirectory)
  {
    var startInfo = new ProcessStartInfo
    {
      CreateNoWindow = true,
      RedirectStandardError = true,
      RedirectStandardInput = true,
      RedirectStandardOutput = true,
      StandardErrorEncoding = Encoding.UTF8,
      StandardOutputEncoding = Encoding.UTF8,
      UseShellExecute = false,
      WorkingDirectory = workingDirectory,
    };

    if (OperatingSystem.IsWindows() && shell != ScriptShell.Bash)
    {
      // Shells write the console code page, which cannot hold most of Unicode, so
      // reading it as UTF-8 corrupts output. Switching the hidden console first keeps
      // the script file and its exit code untouched, and also covers native tools.
      startInfo.FileName = "cmd.exe";
      startInfo.Arguments = $"/d /s /c \"chcp 65001 >nul & {GetWindowsCommand(shell, scriptPath)}\"";
      return startInfo;
    }

    switch (shell)
    {
      case ScriptShell.PowerShell:
        startInfo.FileName = "pwsh";
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        break;
      case ScriptShell.Bash:
        startInfo.FileName = "bash";
        startInfo.ArgumentList.Add(scriptPath);
        break;
      default:
        throw new ArgumentOutOfRangeException(nameof(shell), shell, "Unknown script shell.");
    }

    return startInfo;
  }

  internal static string NormalizeContent(ScriptShell shell, string content)
  {
    return content.ReplaceLineEndings(shell == ScriptShell.Bash ? "\n" : "\r\n");
  }

  private static string GetExtension(ScriptShell shell)
  {
    return shell switch
    {
      ScriptShell.Bash => ".sh",
      ScriptShell.Cmd => ".cmd",
      _ => ".ps1",
    };
  }

  private static string GetWindowsCommand(ScriptShell shell, string scriptPath)
  {
    const string powerShellArguments = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File";

    return shell switch
    {
      ScriptShell.WindowsPowerShell => $"powershell.exe {powerShellArguments} \"{scriptPath}\"",
      ScriptShell.PowerShell => $"pwsh {powerShellArguments} \"{scriptPath}\"",
      ScriptShell.Cmd => $"\"{scriptPath}\"",
      _ => throw new ArgumentOutOfRangeException(nameof(shell), shell, "Unknown script shell."),
    };
  }

  private static bool IsShellSupported(ScriptShell shell)
  {
    return shell switch
    {
      ScriptShell.WindowsPowerShell or ScriptShell.Cmd => OperatingSystem.IsWindows(),
      ScriptShell.PowerShell or ScriptShell.Bash => true,
      _ => false,
    };
  }

  private static async Task ReadCapped(StreamReader reader, CappedOutput output)
  {
    var buffer = new char[4096];
    int read;

    // Keep draining past the cap so a chatty script never blocks on a full pipe.
    while ((read = await reader.ReadAsync(buffer)) > 0)
    {
      output.Append(buffer.AsSpan(0, read));
    }
  }

  private ScriptExecutionResultDto Failed(RunScriptRequestDto request, DateTimeOffset startedAt, string reason)
  {
    return new ScriptExecutionResultDto(
      request.ScriptResultId,
      ExitCode: null,
      StandardOutput: string.Empty,
      StandardError: string.Empty,
      OutputTruncated: false,
      TimedOut: false,
      FailureReason: reason,
      StartedAt: startedAt,
      FinishedAt: _timeProvider.GetUtcNow());
  }

  private async Task<ScriptExecutionResultDto> RunProcess(
    RunScriptRequestDto request,
    string scriptPath,
    DateTimeOffset startedAt,
    CancellationToken cancellationToken)
  {
    var workingDirectory = Path.GetDirectoryName(scriptPath)!;
    var startInfo = CreateStartInfo(request.Shell, scriptPath, workingDirectory);
    var timeoutSeconds = Math.Clamp(request.TimeoutSeconds, ScriptLimits.MinTimeoutSeconds, ScriptLimits.MaxTimeoutSeconds);

    using var process = _processManager.Start(startInfo)
      ?? throw new InvalidOperationException("The script process did not start.");

    // Nothing will ever answer a prompt, so a script that reads stdin gets EOF instead of hanging.
    process.StandardInput.Close();

    var stdout = new CappedOutput(ScriptLimits.MaxOutputBytesPerStream);
    var stderr = new CappedOutput(ScriptLimits.MaxOutputBytesPerStream);
    var readers = Task.WhenAll(
      ReadCapped(process.StandardOutput, stdout),
      ReadCapped(process.StandardError, stderr));

    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds), _timeProvider);
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

    var timedOut = false;
    try
    {
      await process.WaitForExitAsync(linkedCts.Token);
    }
    catch (OperationCanceledException)
    {
      timedOut = timeoutCts.IsCancellationRequested;
      process.Kill(entireProcessTree: true);
      await process.WaitForExitAsync(CancellationToken.None);
    }

    try
    {
      await readers.WaitAsync(_outputDrainTimeout, CancellationToken.None);
    }
    catch (TimeoutException)
    {
      // A background child can hold the pipes open after the shell exits.
      _logger.LogWarning("Script result {ScriptResultId} left its output pipes open.", request.ScriptResultId);
    }

    var stopped = timedOut || cancellationToken.IsCancellationRequested;

    return new ScriptExecutionResultDto(
      request.ScriptResultId,
      ExitCode: stopped ? null : process.ExitCode,
      StandardOutput: stdout.ToString(),
      StandardError: stderr.ToString(),
      OutputTruncated: stdout.IsTruncated || stderr.IsTruncated,
      TimedOut: timedOut,
      FailureReason: timedOut
        ? $"The script was stopped after {timeoutSeconds} seconds."
        : stopped ? "The agent stopped while the script was running." : null,
      StartedAt: startedAt,
      FinishedAt: _timeProvider.GetUtcNow());
  }

  private void TryDeleteScript(string? scriptPath)
  {
    if (scriptPath is null)
    {
      return;
    }

    try
    {
      _fileSystem.DeleteFile(scriptPath);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to delete script file {ScriptPath}.", scriptPath);
    }
  }

  private async Task<string> WriteScriptFile(RunScriptRequestDto request, CancellationToken cancellationToken)
  {
    // The agent runs as SYSTEM/root. The script must never sit in a world-writable
    // directory such as /tmp, where a local user could swap it before it executes.
    var directory = Path.Combine(_pathProvider.GetAgentInstallDirectory(), ScriptDirectoryName);
    _fileSystem.CreateDirectory(directory);

    var scriptPath = Path.Combine(directory, $"{request.ScriptResultId:N}{GetExtension(request.Shell)}");
    var content = NormalizeContent(request.Shell, request.Content);

    // Windows PowerShell reads a BOM-less file as ANSI. Cmd and Bash choke on a BOM.
    var encoding = new UTF8Encoding(
      encoderShouldEmitUTF8Identifier: request.Shell is ScriptShell.WindowsPowerShell or ScriptShell.PowerShell);

    byte[] bytes = [.. encoding.GetPreamble(), .. encoding.GetBytes(content)];
    await _fileSystem.WriteAllBytesAsync(scriptPath, bytes, cancellationToken);
    return scriptPath;
  }
}
