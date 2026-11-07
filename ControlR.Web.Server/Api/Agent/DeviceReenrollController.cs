using System.Security.Cryptography;
using System.Text;
using ControlR.Libraries.Branding;
using ControlR.Libraries.Shared.Services.Encryption;
using ControlR.Web.Server.Extensions.Dtos.Internal;
using ControlR.Web.Server.Services.DeviceManagement;
using ControlR.Web.Server.Services.Settings;
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
  IFleetSecretManager fleetSecretManager,
  ILogger<DeviceReenrollController> logger) : ControllerBase
{
  private const int MinSecretLength = 16;

  private readonly IAgentVersionProvider _agentVersionProvider = agentVersionProvider;
  private readonly IOptionsMonitor<AppOptions> _appOptions = appOptions;
  private readonly IDeviceManager _deviceManager = deviceManager;
  private readonly IFleetSecretManager _fleetSecretManager = fleetSecretManager;
  private readonly IEd25519KeyProvider _keyProvider = keyProvider;
  private readonly ILogger<DeviceReenrollController> _logger = logger;

  [HttpPost]
  [ProducesResponseType(typeof(InternalDtos.DeviceResponseDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status403Forbidden)]
  public async Task<ActionResult<InternalDtos.DeviceResponseDto>> Reenroll(
    [FromBody] InternalDtos.ReenrollDeviceRequestDto request)
  {
    if (request.Device.Id == Guid.Empty || request.Device.TenantId == Guid.Empty)
    {
      return BadRequest();
    }

    if (!await IsAuthorized(request))
    {
      // The log line distinguishes "nothing to match against" from "a secret was offered but wrong".
      _logger.LogWarning(
        "Re-enrollment refused for device {DeviceId}: the fleet secret did not match any accepted secret.",
        request.Device.Id);
      return Forbid();
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

  // A request is authorized if it matches any accepted secret: the tenant's server-generated active
  // secret (strong, rotatable, not in any binary), the manual server-config secret, or the baked-in
  // bootstrap secret (the recovery credential that survives a total database loss).
  private async Task<bool> IsAuthorized(InternalDtos.ReenrollDeviceRequestDto request)
  {
    if (await _fleetSecretManager.Matches(request.Device.TenantId, request.FleetSecret))
    {
      return true;
    }

    return MatchesStaticSecret(_appOptions.CurrentValue.FleetEnrollmentSecret, request.FleetSecret)
      || MatchesStaticSecret(BrandingConstants.FleetEnrollmentSecret, request.FleetSecret);
  }

  private static bool MatchesStaticSecret(string? configured, string provided)
  {
    if (string.IsNullOrWhiteSpace(configured) || configured.Trim().Length < MinSecretLength)
    {
      return false;
    }

    // Constant time, so a wrong secret cannot be found one character at a time by timing the reply.
    var a = Encoding.UTF8.GetBytes(configured.Trim());
    var b = Encoding.UTF8.GetBytes(provided);
    return CryptographicOperations.FixedTimeEquals(a, b);
  }
}
