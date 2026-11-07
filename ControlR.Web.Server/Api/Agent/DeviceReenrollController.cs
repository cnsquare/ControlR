using System.Security.Cryptography;
using System.Text;
using ControlR.Libraries.Branding;
using ControlR.Libraries.Shared.Services.Encryption;
using ControlR.Web.Server.Extensions.Dtos.Internal;
using ControlR.Web.Server.Services.DeviceManagement;
using Microsoft.AspNetCore.Mvc;

namespace ControlR.Web.Server.Api.Agent;

/// <summary>
/// Re-creates a device record for an agent the server no longer knows, authorized only by the
/// fleet secret. This exists so a fleet survives a lost-and-rebuilt database. The effective secret
/// is <see cref="AppOptions.FleetEnrollmentSecret"/> if set, otherwise the one baked into the build
/// (<see cref="BrandingConstants.FleetEnrollmentSecret"/>). It is off only when neither is set, and
/// then refuses any request; otherwise it refuses any request whose secret does not match.
/// </summary>
[Route(HttpConstants.Agent.DeviceReenrollEndpoint)]
[ApiController]
[AllowAnonymous]
[EndpointGroupName(OpenApiConstants.InternalGroupName)]
public class DeviceReenrollController(
  IOptionsMonitor<AppOptions> appOptions,
  IEd25519KeyProvider keyProvider,
  IDeviceManager deviceManager,
  IAgentVersionProvider agentVersionProvider,
  ILogger<DeviceReenrollController> logger) : ControllerBase
{
  private const int MinSecretLength = 16;

  private readonly IAgentVersionProvider _agentVersionProvider = agentVersionProvider;
  private readonly IOptionsMonitor<AppOptions> _appOptions = appOptions;
  private readonly IDeviceManager _deviceManager = deviceManager;
  private readonly IEd25519KeyProvider _keyProvider = keyProvider;
  private readonly ILogger<DeviceReenrollController> _logger = logger;

  [HttpPost]
  [ProducesResponseType(typeof(InternalDtos.DeviceResponseDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status403Forbidden)]
  public async Task<ActionResult<InternalDtos.DeviceResponseDto>> Reenroll(
    [FromBody] InternalDtos.ReenrollDeviceRequestDto request)
  {
    // Server config wins; otherwise the build's baked-in secret makes re-enrollment standard.
    var configuredSecret = _appOptions.CurrentValue.FleetEnrollmentSecret;
    if (string.IsNullOrWhiteSpace(configuredSecret))
    {
      configuredSecret = BrandingConstants.FleetEnrollmentSecret;
    }

    // Off only when no secret is set anywhere: then nothing can be created this way.
    if (string.IsNullOrWhiteSpace(configuredSecret) || configuredSecret.Trim().Length < MinSecretLength)
    {
      _logger.LogWarning("Re-enrollment attempted but no fleet secret is configured.");
      return Forbid();
    }

    if (!SecretsMatch(configuredSecret.Trim(), request.FleetSecret))
    {
      _logger.LogWarning(
        "Re-enrollment refused for device {DeviceId}: the fleet secret did not match.",
        request.Device.Id);
      return Forbid();
    }

    if (request.Device.Id == Guid.Empty || request.Device.TenantId == Guid.Empty)
    {
      return BadRequest();
    }

    // The agent keeps its existing key, so the record it rebuilds trusts the same identity it had.
    if (!_keyProvider.ValidatePublicKeyBase64(request.PublicKey).IsSuccess)
    {
      _logger.LogWarning("Re-enrollment refused for device {DeviceId}: invalid public key.", request.Device.Id);
      return BadRequest();
    }

    var connectionContext = new DeviceConnectionContext(
      ConnectionId: string.Empty,
      RemoteIpAddress: HttpContext.Connection.RemoteIpAddress,
      LastSeen: DateTimeOffset.UtcNow,
      IsOnline: false);

    var entity = await _deviceManager.AddOrUpdate(request.Device, connectionContext, tagIds: null, request.PublicKey);

    _logger.LogInformation(
      "Device {DeviceId} re-enrolled into tenant {TenantId} with the fleet secret.",
      entity.Id,
      entity.TenantId);

    var isOutdated = await _agentVersionProvider.IsAgentOutdated(entity.AgentVersion);
    return entity.ToInternalResponseDto(isOutdated);
  }

  private static bool SecretsMatch(string configured, string provided)
  {
    // Constant time, so a wrong secret cannot be found one character at a time by timing the reply.
    var a = Encoding.UTF8.GetBytes(configured);
    var b = Encoding.UTF8.GetBytes(provided);
    return CryptographicOperations.FixedTimeEquals(a, b);
  }
}
