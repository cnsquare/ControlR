using ControlR.Assist.Services;
using ControlR.DesktopClient.Common.Options;
using ControlR.Libraries.Api.Contracts.Authz;
using ControlR.Libraries.Api.Contracts.Dtos.HubDtos;
using ControlR.Libraries.Avalonia.Services;
using ControlR.Libraries.Shared.Services.Locks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ControlR.Assist.Tests;

public class UnattendedAccessTests
{
  [Fact]
  public void BuildInstallArguments_UsesTheGrantAndNamesTheInstanceAfterTheServer()
  {
    var tenantId = Guid.NewGuid();
    var keyId = Guid.NewGuid();

    var arguments = UnattendedAccessInstaller.BuildInstallArguments(
      new Uri("https://support.example.com"),
      new UnattendedAccessGrantDto(tenantId, keyId, "secret"));

    Assert.StartsWith("install -s https://support.example.com/", arguments);
    Assert.Contains($"-t {tenantId}", arguments);
    Assert.Contains("-ks secret", arguments);
    Assert.Contains($"-ki {keyId}", arguments);
    Assert.EndsWith("-i support.example.com", arguments);
  }

  [Fact]
  public void SessionHost_GrantsWhatATechnicianNeedsAndNothingThatCouldLockOutTheCustomer()
  {
    Assert.SkipUnless(OperatingSystem.IsWindows(), "The session host needs a desktop environment.");

    using var appServices = new ServiceCollection()
      .AddLogging()
      .AddSingleton<IUiDispatcher, UiDispatcher>()
      .AddSingleton<IAsyncLock, KeyedLock>()
      .BuildServiceProvider();

    var factory = new AssistSessionHostFactory(
      appServices.GetRequiredService<ILoggerFactory>(),
      appServices.GetRequiredService<IUiDispatcher>(),
      appServices.GetRequiredService<IAsyncLock>());

    using var host = factory.CreateSessionHost(new AssistSessionRequest(
      new Uri("https://server.example.com"),
      Guid.NewGuid(),
      "access-token",
      "Viewer"));

    var allowed = host.Services
      .GetRequiredService<IOptions<RemoteControlSessionOptions>>()
      .Value
      .AllowedPermissions;

    Assert.Contains(PermissionNames.DeviceRemoteControlConnect, allowed);
    Assert.Contains(PermissionNames.DeviceRemoteControlInteract, allowed);
    Assert.DoesNotContain(PermissionNames.DeviceRemoteControlAudio, allowed);
    Assert.DoesNotContain(PermissionNames.DeviceRemoteControlBlockInput, allowed);
    Assert.DoesNotContain(PermissionNames.DeviceRemoteControlPrivacyScreen, allowed);
  }
}
