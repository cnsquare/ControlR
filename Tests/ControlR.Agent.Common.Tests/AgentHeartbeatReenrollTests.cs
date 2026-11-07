using System.Runtime.InteropServices;
using ControlR.Agent.Common.Services;
using ControlR.Agent.Shared.Services;
using ControlR.ApiClient;
using ControlR.ApiClient.Interfaces.Agent;
using ControlR.Libraries.Api.Contracts.Dtos;
using ControlR.Libraries.Api.Contracts.Dtos.Devices;
using ControlR.Agent.Shared.Interfaces;
using ControlR.Libraries.Api.Contracts.Dtos.HubDtos;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.Internal;
using ControlR.Libraries.Api.Contracts.Enums;
using ControlR.Libraries.Api.Contracts.Hubs;
using ControlR.Libraries.Shared.Extensions;
using ControlR.Libraries.Shared.Services;
using ControlR.Libraries.Shared.Services.Encryption;
using ControlR.Libraries.Signalr.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ControlR.Agent.Common.Tests;

public class AgentHeartbeatReenrollTests
{
  private const string FleetSecret = "a-very-long-fleet-secret-value";

  [Fact]
  public async Task Heartbeat_WhenServerForgotDevice_AndSecretSet_ReenrollsAndRetries()
  {
    var fixture = new Fixture { FleetSecret = FleetSecret };
    fixture.QueueHeartbeatResults(
      HubResult.Fail<DeviceResponseDto>("Unknown device."),
      HubResult.Ok(fixture.Device.CloneAs<DeviceResponseDto>()));

    var timer = fixture.Create();
    await timer.SendDeviceHeartbeat();

    fixture.Devices.Verify(
      x => x.ReenrollDevice(
        It.Is<ReenrollDeviceRequestDto>(r => r.FleetSecret == FleetSecret && r.Device.Id == fixture.Device.Id),
        It.IsAny<CancellationToken>()),
      Times.Once);

    // The heartbeat is retried after a successful re-enrollment.
    fixture.Hub.Verify(x => x.UpdateDeviceSigned(It.IsAny<SignedDto<DeviceUpdateRequestDto>>()), Times.Exactly(2));
  }

  [Fact]
  public async Task Heartbeat_WhenServerForgotDevice_AndNoSecret_DoesNotReenroll()
  {
    var fixture = new Fixture { FleetSecret = null };
    fixture.QueueHeartbeatResults(HubResult.Fail<DeviceResponseDto>("Unknown device."));

    var timer = fixture.Create();
    await timer.SendDeviceHeartbeat();

    fixture.Devices.Verify(
      x => x.ReenrollDevice(It.IsAny<ReenrollDeviceRequestDto>(), It.IsAny<CancellationToken>()),
      Times.Never);
    fixture.Hub.Verify(x => x.UpdateDeviceSigned(It.IsAny<SignedDto<DeviceUpdateRequestDto>>()), Times.Once);
  }

  [Fact]
  public async Task Heartbeat_AdoptsServerActiveSecret()
  {
    var fixture = new Fixture { FleetSecret = null, ServerActiveSecret = "server-generated-active-secret" };
    fixture.QueueHeartbeatResults(HubResult.Ok(fixture.Device.CloneAs<DeviceResponseDto>()));

    var timer = fixture.Create();
    await timer.SendDeviceHeartbeat();

    fixture.Options.Verify(
      x => x.UpdateFleetEnrollmentSecret("server-generated-active-secret"),
      Times.Once);
  }

  private sealed class Fixture
  {
    public string? FleetSecret { get; init; }

    public string? ServerActiveSecret { get; init; }

    public DeviceUpdateRequestDto Device { get; } = CreateDevice();

    public Mock<IAgentHub> Hub { get; } = new();
    public Mock<IAgentDevicesApi> Devices { get; } = new();
    public Mock<IOptionsAccessor> Options { get; } = new();

    public void QueueHeartbeatResults(params HubResult<DeviceResponseDto>[] results)
    {
      var sequence = Hub.SetupSequence(x => x.UpdateDeviceSigned(It.IsAny<SignedDto<DeviceUpdateRequestDto>>()));
      foreach (var result in results)
      {
        sequence = sequence.ReturnsAsync(result);
      }
    }

    public AgentHeartbeatTimer Create()
    {
      var hubConnection = new Mock<IHubConnection<IAgentHub>>();
      hubConnection.Setup(x => x.ConnectionState).Returns(HubConnectionState.Connected);
      hubConnection.Setup(x => x.Server).Returns(Hub.Object);
      Hub.Setup(x => x.GetFleetEnrollmentSecret()).ReturnsAsync(ServerActiveSecret);

      var deviceInfo = new Mock<IDeviceInfoProvider>();
      deviceInfo.Setup(x => x.GetDeviceInfo()).ReturnsAsync(Device);

      var options = Options;
      options.Setup(x => x.PrivateKey).Returns(Convert.ToBase64String(new byte[32]));
      options.Setup(x => x.AgentDisplayName).Returns("Fixed Name");
      options.Setup(x => x.FleetEnrollmentSecret).Returns(FleetSecret);

      var keyProvider = new Mock<IEd25519KeyProvider>();
      keyProvider.Setup(x => x.DerivePublicKeyBase64(It.IsAny<string>())).Returns("public-key-base64");
      keyProvider
        .Setup(x => x.Sign(It.IsAny<DeviceUpdateRequestDto>(), It.IsAny<byte[]>(), It.IsAny<string?>()))
        .Returns(new SignedDto<DeviceUpdateRequestDto>(Device, DateTimeOffset.UtcNow, [], null));

      var agentApi = new Mock<IControlrAgentApi>();
      agentApi.Setup(x => x.Devices).Returns(Devices.Object);
      Devices
        .Setup(x => x.ReenrollDevice(It.IsAny<ReenrollDeviceRequestDto>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(ApiResult.Ok());

      var controlrApi = new Mock<IControlrApi>();
      controlrApi.Setup(x => x.Agent).Returns(agentApi.Object);

      var displayNameApplier = new Mock<IAgentDisplayNameApplier>();
      displayNameApplier
        .Setup(x => x.Apply(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

      var systemEnvironment = new Mock<ISystemEnvironment>();

      return new AgentHeartbeatTimer(
        TimeProvider.System,
        hubConnection.Object,
        systemEnvironment.Object,
        deviceInfo.Object,
        options.Object,
        keyProvider.Object,
        controlrApi.Object,
        displayNameApplier.Object,
        NullLogger<AgentHeartbeatTimer>.Instance);
    }
  }

  private static DeviceUpdateRequestDto CreateDevice()
  {
    var id = Guid.NewGuid();
    return new DeviceUpdateRequestDto(
      Name: $"Test Device {id}",
      AgentVersion: "1.0.0",
      CpuUtilization: 25.0,
      Id: id,
      Is64Bit: true,
      OsArchitecture: Architecture.X64,
      Platform: SystemPlatform.Windows,
      ProcessorCount: 8,
      OsDescription: "Windows 11 Pro",
      TenantId: Guid.NewGuid(),
      TotalMemory: 16384,
      TotalStorage: 512000,
      UsedMemory: 8192,
      UsedStorage: 256000,
      CurrentUsers: ["TestUser"],
      MacAddresses: ["00:11:22:33:44:55"],
      LocalIpV4: "10.0.0.1",
      LocalIpV6: "fe80::1",
      Drives: [new Drive { Name = "C:", VolumeLabel = "System", TotalSize = 512000, FreeSpace = 256000 }]);
  }
}
