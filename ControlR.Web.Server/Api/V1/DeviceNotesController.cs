using Asp.Versioning;
using ControlR.Web.Server.Constants;
using Microsoft.AspNetCore.Mvc;

namespace ControlR.Web.Server.Api.V1;

/// <summary>
/// Free-text notes technicians keep about a device.
/// </summary>
[Route(HttpConstants.V1.DeviceNotesEndpoint)]
[ApiController]
[Authorize]
[ApiVersion(ApiVersions.V1)]
public class DeviceNotesController(
  AppDb appDb,
  IAuthorizationService authorizationService) : ControllerBase
{
  private readonly AppDb _appDb = appDb;
  private readonly IAuthorizationService _authorizationService = authorizationService;

  [HttpGet("{deviceId:guid}")]
  [ProducesResponseType(typeof(V1Dtos.DeviceNotesDto), StatusCodes.Status200OK)]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
  public async Task<ActionResult<V1Dtos.DeviceNotesDto>> GetDeviceNotes(
    [FromRoute] Guid deviceId,
    [FromQuery] Guid tenantId,
    CancellationToken cancellationToken)
  {
    if (!User.TryResolveTenantId(tenantId, out var resolvedTenantId))
    {
      return Forbid();
    }

    var device = await _appDb.Devices
      .AsNoTracking()
      .FirstOrDefaultAsync(x => x.Id == deviceId && x.TenantId == resolvedTenantId, cancellationToken);

    if (device is null)
    {
      return Problem(statusCode: StatusCodes.Status404NotFound, title: V1ProblemTitles.NotFound);
    }

    var authResult = await _authorizationService.AuthorizeAsync(User, device, DeviceResourcePolicies.Read);
    return authResult.Succeeded
      ? Ok(new V1Dtos.DeviceNotesDto(device.Notes))
      : Forbid();
  }

  [HttpPut("{deviceId:guid}")]
  [ProducesResponseType(typeof(V1Dtos.DeviceNotesDto), StatusCodes.Status200OK)]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
  public async Task<ActionResult<V1Dtos.DeviceNotesDto>> UpdateDeviceNotes(
    [FromRoute] Guid deviceId,
    [FromBody] V1Dtos.DeviceNotesDto request,
    [FromQuery] Guid tenantId,
    CancellationToken cancellationToken)
  {
    if (!User.TryResolveTenantId(tenantId, out var resolvedTenantId))
    {
      return Forbid();
    }

    var device = await _appDb.Devices
      .FirstOrDefaultAsync(x => x.Id == deviceId && x.TenantId == resolvedTenantId, cancellationToken);

    if (device is null)
    {
      return Problem(statusCode: StatusCodes.Status404NotFound, title: V1ProblemTitles.NotFound);
    }

    var authResult = await _authorizationService.AuthorizeAsync(User, device, DeviceResourcePolicies.NotesWrite);
    if (!authResult.Succeeded)
    {
      return Forbid();
    }

    device.Notes = request.Notes;
    await _appDb.SaveChangesAsync(cancellationToken);
    return Ok(new V1Dtos.DeviceNotesDto(device.Notes));
  }
}
