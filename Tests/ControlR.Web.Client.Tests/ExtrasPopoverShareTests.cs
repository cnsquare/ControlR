using System.Runtime.InteropServices;
using Bunit;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.Internal;
using ControlR.Libraries.Api.Contracts.Enums;
using ControlR.Libraries.Viewer.Common;
using ControlR.Libraries.Viewer.Common.State;
using ControlR.Web.Client.Components.RemoteDisplays;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MudBlazor;
using MudBlazor.Services;

namespace ControlR.Web.Client.Tests;

/// <summary>
/// The "Share" button mints a no-login link to a device. A Flash Assist session has no device record
/// (and the relay is single-viewer), so the button must not appear there. It is gated on
/// <see cref="IDeviceState.IsDeviceLoaded"/>, which is false in an assist session and true for a device
/// remote-control session.
/// </summary>
public class ExtrasPopoverShareTests
{
  // The button's visible label. Metrics/DirectX are the only other controls and never contain it.
  private const string ShareLabel = "Share";

  [Fact]
  public async Task ShareButton_HiddenInAssistMode_NoDeviceLoaded()
  {
    await using var ctx = CreateContext(deviceLoaded: false);
    var cut = ctx.Render<ExtrasPopover>();
    Assert.DoesNotContain(ShareLabel, cut.Markup);
  }

  [Fact]
  public async Task ShareButton_ShownForDeviceSession_DeviceLoaded()
  {
    await using var ctx = CreateContext(deviceLoaded: true);
    var cut = ctx.Render<ExtrasPopover>();
    Assert.Contains(ShareLabel, cut.Markup);
  }

  private static BunitContext CreateContext(bool deviceLoaded)
  {
    var ctx = new BunitContext();

    var deviceState = new Mock<IDeviceState>();
    deviceState.SetupGet(x => x.IsDeviceLoaded).Returns(deviceLoaded);
    if (deviceLoaded)
    {
      var device = Device(SystemPlatform.Windows);
      deviceState.SetupGet(x => x.CurrentDevice).Returns(device);
      deviceState.Setup(x => x.TryGetCurrentDevice()).Returns(device);
    }

    var remoteControlState = new Mock<IRemoteControlState>();
    remoteControlState.Setup(x => x.OnStateChanged(It.IsAny<Func<Task>>())).Returns(Mock.Of<IDisposable>());

    ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    ctx.Services.AddMudServices();
    ctx.Services.AddSingleton(deviceState.Object);
    ctx.Services.AddSingleton(remoteControlState.Object);
    ctx.Services.AddSingleton(Mock.Of<IViewerRemoteControlStream>());
    ctx.Services.AddSingleton(Mock.Of<ISnackbar>());
    ctx.Services.AddSingleton(Mock.Of<IDialogService>());
    ctx.Services.AddSingleton<ILogger<ExtrasPopover>>(NullLogger<ExtrasPopover>.Instance);

    ctx.Render<MudPopoverProvider>();
    return ctx;
  }

  private static DeviceResponseDto Device(SystemPlatform platform) =>
    new(
      Name: "device",
      AgentVersion: "1.0.0",
      CpuUtilization: 0,
      Id: Guid.NewGuid(),
      Is64Bit: true,
      IsOnline: true,
      LastSeen: DateTimeOffset.UtcNow,
      OsArchitecture: Architecture.X64,
      Platform: platform,
      ProcessorCount: 1,
      ConnectionId: "connection",
      OsDescription: "os",
      TenantId: Guid.NewGuid(),
      TotalMemory: 0,
      TotalStorage: 0,
      UsedMemory: 0,
      UsedStorage: 0,
      CurrentUsers: [],
      MacAddresses: [],
      PublicIpV4: string.Empty,
      PublicIpV6: string.Empty,
      LocalIpV4: string.Empty,
      LocalIpV6: string.Empty,
      Drives: [],
      IsOutdated: false);
}
