using ControlR.Libraries.Shared.Logging;
using ControlR.Libraries.Shared.Services.Encryption;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;

namespace ControlR.Agent.Common.Services;

internal interface IAgentHeartbeatTimer : IHostedService
{
  Task SendDeviceHeartbeat();
}

internal class AgentHeartbeatTimer(
  TimeProvider timeProvider,
  IHubConnection<IAgentHub> hubConnection,
  ISystemEnvironment systemEnvironment,
  IDeviceInfoProvider deviceDataGenerator,
  IOptionsAccessor optionsAccessor,
  IEd25519KeyProvider keyProvider,
  IControlrApi controlrApi,
  IAgentDisplayNameApplier displayNameApplier,
  ILogger<AgentHeartbeatTimer> logger) : BackgroundService, IAgentHeartbeatTimer
{
  private readonly IAgentDisplayNameApplier _displayNameApplier = displayNameApplier;
  private readonly IDeviceInfoProvider _deviceDataGenerator = deviceDataGenerator;
  private readonly IHubConnection<IAgentHub> _hubConnection = hubConnection;
  private readonly IControlrApi _controlrApi = controlrApi;
  private readonly IEd25519KeyProvider _keyProvider = keyProvider;
  private readonly ILogger<AgentHeartbeatTimer> _logger = logger;
  private readonly IOptionsAccessor _optionsAccessor = optionsAccessor;
  private readonly ISystemEnvironment _systemEnvironment = systemEnvironment;
  private readonly TimeProvider _timeProvider = timeProvider;
  
  public async Task SendDeviceHeartbeat()
  {
    try
    {
      using var _ = _logger.BeginMemberScope();

      if (_hubConnection.ConnectionState != HubConnectionState.Connected)
      {
        _logger.LogWarning("Not connected to hub when trying to send device update.");
        return;
      }

      var device = await _deviceDataGenerator.GetDeviceInfo();

      var dto = device.CloneAs<DeviceUpdateRequestDto>();

      var privateKeyBase64 = _optionsAccessor.PrivateKey;
      string? publicKeyBase64 = null;
      
      if (string.IsNullOrWhiteSpace(privateKeyBase64))
      {
        _logger.LogInformationDeduped("No private key found. Generating new Ed25519 keypair for identity bootstrapping.");
        var keyPair = _keyProvider.GenerateKeyPair();
        privateKeyBase64 = Convert.ToBase64String(keyPair.PrivateKey);
        publicKeyBase64 = Convert.ToBase64String(keyPair.PublicKey);

        await _optionsAccessor.UpdatePrivateKey(privateKeyBase64);

        _logger.LogInformationDeduped("New keypair generated and saved.");
      }
      else
      {
        _logger.LogDebugDeduped("Private key found. Using existing key for heartbeat.");
        publicKeyBase64 = _keyProvider.DerivePublicKeyBase64(privateKeyBase64);
      }

      var privateKey = Convert.FromBase64String(privateKeyBase64);
      var signedDto = _keyProvider.Sign(dto, privateKey, publicKeyBase64);
      var updateResult = await _hubConnection.Server.UpdateDeviceSigned(signedDto);

      if (!updateResult.IsSuccess)
      {
        _logger.LogResult(updateResult);

        // A rebuilt server no longer knows this device. If a fleet secret is configured, the agent
        // re-creates its own record with the key it already holds, then retries the heartbeat once.
        if (!IsReenrollableRejection(updateResult.Reason) ||
            !await TryReenroll(dto, publicKeyBase64))
        {
          return;
        }

        _logger.LogInformation("Re-enrollment succeeded. Retrying the heartbeat.");
        updateResult = await _hubConnection.Server.UpdateDeviceSigned(signedDto);
        if (!updateResult.IsSuccess)
        {
          _logger.LogResult(updateResult);
          return;
        }
      }

      if (updateResult.Value.Id != device.Id)
      {
        _logger.LogInformation("Device ID changed.  Updating appsettings.");
        await _optionsAccessor.UpdateId(updateResult.Value.Id);
      }

      await ApplyDisplayName();
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error while sending device update.");
    }
  }

  // Matches the hub's rejection reasons for a device the rebuilt server has lost.
  private static bool IsReenrollableRejection(string? reason) =>
    reason is "Unknown device." or "Device requires enrollment.";

  // Re-creates this device on the server after a database loss, authorized by the shared fleet
  // secret. Does nothing, and stays inert, unless a secret is configured on this install.
  private async Task<bool> TryReenroll(DeviceUpdateRequestDto dto, string publicKeyBase64)
  {
    var fleetSecret = _optionsAccessor.FleetEnrollmentSecret;
    if (string.IsNullOrWhiteSpace(fleetSecret))
    {
      _logger.LogDebugDeduped(
        "The server no longer recognizes this device, but no fleet enrollment secret is configured, so it cannot re-enroll itself.");
      return false;
    }

    _logger.LogWarning(
      "The server no longer recognizes this device. Attempting re-enrollment with the fleet secret.");

    var request = new ReenrollDeviceRequestDto(dto, publicKeyBase64, fleetSecret);
    var result = await _controlrApi.Agent.Devices.ReenrollDevice(request);
    if (!result.IsSuccess)
    {
      _logger.LogError("Re-enrollment failed. Reason: {Reason}", result.Reason);
      return false;
    }

    return true;
  }

  // Kept apart from the heartbeat, because a problem renaming must not look like a failed update.
  private async Task ApplyDisplayName()
  {
    try
    {
      // A name given to this install wins over the tenant's.
      var displayName = string.IsNullOrWhiteSpace(_optionsAccessor.AgentDisplayName)
        ? await _hubConnection.Server.GetAgentDisplayName()
        : _optionsAccessor.AgentDisplayName;

      await _displayNameApplier.Apply(displayName, CancellationToken.None);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to apply the agent display name.");
    }
  }


  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    var delayTime = _systemEnvironment.IsDebug ?
        TimeSpan.FromSeconds(10) :
        TimeSpan.FromMinutes(5);

    using var timer = new PeriodicTimer(delayTime, _timeProvider);
    try
    {
      while (await timer.WaitForNextTickAsync(stoppingToken))
      {
        try
        {
          await SendDeviceHeartbeat();
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "Error while sending agent heartbeat.");
        }
      }
    }
    catch (OperationCanceledException)
    {
      _logger.LogInformation("Heartbeat aborted.  Application shutting down.");
    }
  }
}
