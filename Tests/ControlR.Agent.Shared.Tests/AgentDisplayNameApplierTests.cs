using System.Runtime.Versioning;
using ControlR.Agent.Shared.Options;
using ControlR.Agent.Shared.Services;
using ControlR.Agent.Shared.Services.Windows;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ControlR.Agent.Shared.Tests;

[SupportedOSPlatform("windows")]
public class AgentDisplayNameApplierTests
{
  [Fact]
  public async Task Apply_NotRunningAsAService_ChangesNothing()
  {
    if (!OperatingSystem.IsWindows())
    {
      Assert.Skip("Windows only.");
    }

    var paths = new Mock<IFileSystemPathProvider>(MockBehavior.Strict);
    var applier = new AgentDisplayNameApplierWindows(
      Microsoft.Extensions.Options.Options.Create(new InstanceOptions()),
      paths.Object,
      NullLogger<AgentDisplayNameApplierWindows>.Instance);

    await applier.Apply("EnkrOma Support", TestContext.Current.CancellationToken);

    paths.VerifyNoOtherCalls();
  }

  [Fact]
  public void BuildServiceConfigArguments_KeepsEachValueAsOneArgument()
  {
    if (!OperatingSystem.IsWindows())
    {
      Assert.Skip("Windows only.");
    }

    var arguments = AgentDisplayNameApplierWindows.BuildServiceConfigArguments(
      "ControlR.Agent (example.com)",
      "EnkrOma Support");

    Assert.Equal(["config", "ControlR.Agent (example.com)", "DisplayName=", "EnkrOma Support"], arguments);
  }
}
