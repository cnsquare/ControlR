using System.Text;
using ControlR.Agent.Common.Services.Scripting;
using ControlR.Agent.Shared.Services;
using ControlR.Libraries.Api.Contracts.Constants;
using ControlR.Libraries.Api.Contracts.Dtos.HubDtos;
using ControlR.Libraries.Api.Contracts.Enums;
using ControlR.Libraries.Shared.Services.FileSystem;
using ControlR.Libraries.Shared.Services.Processes;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ControlR.Agent.Common.Tests;

public sealed class ScriptExecutorTests : IDisposable
{
  private readonly string _installDirectory = Path.Combine(Path.GetTempPath(), $"script-tests-{Guid.NewGuid():N}");

  public void Dispose()
  {
    if (Directory.Exists(_installDirectory))
    {
      Directory.Delete(_installDirectory, recursive: true);
    }
  }

  [Fact]
  public void CappedOutput_DoesNotSplitSurrogatePair()
  {
    var output = new CappedOutput(maxBytes: 5);

    output.Append("a😀😀");

    Assert.True(output.IsTruncated);
    Assert.Equal("a😀", output.ToString());
  }

  [Fact]
  public void CappedOutput_StaysWithinByteBudgetForMultiByteText()
  {
    var output = new CappedOutput(maxBytes: 10);

    output.Append(new string('語', 100));

    Assert.True(output.IsTruncated);
    Assert.True(Encoding.UTF8.GetByteCount(output.ToString()) <= 10);
  }

  [Fact]
  public async Task Execute_CapsOutputAndStillCompletes()
  {
    var (shell, content) = OperatingSystem.IsWindows()
      ? (ScriptShell.Cmd, "@for /L %%i in (1,1,6000) do @echo 0123456789012345678901234567890123456789")
      : (ScriptShell.Bash, "for i in $(seq 1 6000); do echo 0123456789012345678901234567890123456789; done");

    var result = await CreateExecutor().Execute(Request(shell, content, 60), TestContext.Current.CancellationToken);

    Assert.Equal(0, result.ExitCode);
    Assert.True(result.OutputTruncated);
    Assert.True(Encoding.UTF8.GetByteCount(result.StandardOutput) <= ScriptLimits.MaxOutputBytesPerStream);
  }

  [Fact]
  public async Task Execute_DeletesScriptFileAfterRunning()
  {
    var (shell, content) = EchoScript();

    await CreateExecutor().Execute(Request(shell, content), TestContext.Current.CancellationToken);

    Assert.Empty(Directory.GetFiles(Path.Combine(_installDirectory, "ScriptRuns")));
  }

  [Fact]
  public async Task Execute_ReportsExitCodeAndStandardError()
  {
    var (shell, content) = OperatingSystem.IsWindows()
      ? (ScriptShell.Cmd, "@echo failure 1>&2\r\n@exit /b 7")
      : (ScriptShell.Bash, "echo failure 1>&2\nexit 7");

    var result = await CreateExecutor().Execute(Request(shell, content), TestContext.Current.CancellationToken);

    Assert.Equal(7, result.ExitCode);
    Assert.Contains("failure", result.StandardError);
    Assert.Null(result.FailureReason);
  }

  [Fact]
  public async Task Execute_ReturnsOutputOfSuccessfulScript()
  {
    var (shell, content) = EchoScript();

    var result = await CreateExecutor().Execute(Request(shell, content), TestContext.Current.CancellationToken);

    Assert.Equal(0, result.ExitCode);
    Assert.Contains("hello-script", result.StandardOutput);
    Assert.False(result.TimedOut);
    Assert.False(result.OutputTruncated);
  }

  [Fact]
  public async Task Execute_StopsScriptAtTimeout()
  {
    var (shell, content) = OperatingSystem.IsWindows()
      ? (ScriptShell.WindowsPowerShell, "Start-Sleep -Seconds 60")
      : (ScriptShell.Bash, "sleep 60");

    var started = DateTimeOffset.UtcNow;
    var result = await CreateExecutor().Execute(Request(shell, content, 2), TestContext.Current.CancellationToken);

    Assert.True(result.TimedOut);
    Assert.Null(result.ExitCode);
    Assert.NotNull(result.FailureReason);
    Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(30));
  }

  [Fact]
  public async Task Execute_WhenShellIsWrongForPlatform_FailsWithoutRunning()
  {
    var shell = OperatingSystem.IsWindows() ? (ScriptShell)99 : ScriptShell.Cmd;

    var result = await CreateExecutor().Execute(Request(shell, "echo no"), TestContext.Current.CancellationToken);

    Assert.Null(result.ExitCode);
    Assert.NotNull(result.FailureReason);
    Assert.False(Directory.Exists(Path.Combine(_installDirectory, "ScriptRuns")));
  }

  [Fact]
  public async Task Execute_WritesScriptInsideInstallDirectory()
  {
    var (shell, content) = OperatingSystem.IsWindows()
      ? (ScriptShell.Cmd, "@echo %~dp0")
      : (ScriptShell.Bash, "dirname \"$0\"");

    var result = await CreateExecutor().Execute(Request(shell, content), TestContext.Current.CancellationToken);

    Assert.Contains(_installDirectory, result.StandardOutput);
  }

  [Theory]
  [InlineData(ScriptShell.Bash, "a\r\nb", "a\nb")]
  [InlineData(ScriptShell.Cmd, "a\nb", "a\r\nb")]
  [InlineData(ScriptShell.WindowsPowerShell, "a\nb", "a\r\nb")]
  public void NormalizeContent_UsesLineEndingsTheShellExpects(ScriptShell shell, string input, string expected)
  {
    Assert.Equal(expected, ScriptExecutor.NormalizeContent(shell, input));
  }

  private static (ScriptShell Shell, string Content) EchoScript()
  {
    return OperatingSystem.IsWindows()
      ? (ScriptShell.Cmd, "@echo hello-script")
      : (ScriptShell.Bash, "echo hello-script");
  }

  private static RunScriptRequestDto Request(ScriptShell shell, string content, int timeoutSeconds = 30)
  {
    return new RunScriptRequestDto(Guid.NewGuid(), shell, content, timeoutSeconds);
  }

  private ScriptExecutor CreateExecutor()
  {
    var pathProvider = new Mock<IFileSystemPathProvider>();
    pathProvider.Setup(x => x.GetAgentInstallDirectory()).Returns(_installDirectory);

    return new ScriptExecutor(
      new FileSystem(NullLogger<FileSystem>.Instance),
      pathProvider.Object,
      new ProcessManager(),
      TimeProvider.System,
      NullLogger<ScriptExecutor>.Instance);
  }
}
