using ControlR.Agent.Shared.Interfaces;
using ControlR.Agent.Shared.Options;
using ControlR.Agent.Shared.Services;
using ControlR.Agent.Shared.Services.Base;
using ControlR.ApiClient;
using ControlR.Libraries.Shared.Services;
using ControlR.Libraries.Shared.Services.Encryption;
using ControlR.Libraries.Shared.Services.FileSystem;
using ControlR.Libraries.Shared.Services.Processes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace ControlR.Agent.Shared.Tests;

public class AgentInstallNameTests
{
  private static readonly Uri _server = new("https://example.com");

  [Fact]
  public async Task UpdateAppSettings_NameGiven_IsStoredTrimmed()
  {
    var options = new AgentAppOptions();

    await new NameInstaller(options).Update("  Front Desk  ");

    Assert.Equal("Front Desk", options.AgentDisplayName);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  public async Task UpdateAppSettings_NoName_KeepsTheExistingName(string? name)
  {
    var options = new AgentAppOptions { AgentDisplayName = "Front Desk" };

    await new NameInstaller(options).Update(name);

    Assert.Equal("Front Desk", options.AgentDisplayName);
  }

  private sealed class NameInstaller(AgentAppOptions options)
    : AgentInstallerBase(
      Mock.Of<IFileSystem>(),
      Mock.Of<IFileSystemPathProvider>(),
      Mock.Of<IControlrApi>(),
      Mock.Of<IDeviceInfoProvider>(),
      Mock.Of<IOptionsAccessor>(),
      Mock.Of<IProcessManager>(),
      Mock.Of<ISystemEnvironment>(),
      Mock.Of<IOptionsMonitor<AgentAppOptions>>(x => x.CurrentValue == options),
      NullLogger<AgentInstallerBase>.Instance,
      Mock.Of<IEd25519KeyProvider>())
  {
    public Task Update(string? name) => UpdateAppSettings(_server, Guid.NewGuid(), Guid.NewGuid(), name);
  }
}
