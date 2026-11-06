using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace ControlR.Web.Server.Api.V1;

/// <summary>
/// The history of remote control sessions. It is read-only, because an audit trail that can be
/// edited through the API is not one.
/// </summary>
[Route(HttpConstants.V1.RemoteSessionLogsEndpoint)]
[ApiController]
[Authorize]
[ApiVersion(ApiVersions.V1)]
public class RemoteSessionLogsController(AppDb appDb) : ControllerBase
{
  private const int DefaultTake = 200;
  private const int MaxTake = 1000;

  private readonly AppDb _appDb = appDb;

  /// <summary>
  /// Lists sessions, most recent first.
  /// </summary>
  [HttpGet]
  [Authorize(Policy = PolicyNames.RequireAuthorizationLogsRead)]
  [ProducesResponseType(typeof(V1Dtos.RemoteSessionLogDto[]), StatusCodes.Status200OK)]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
  public async Task<ActionResult<V1Dtos.RemoteSessionLogDto[]>> GetRemoteSessionLogs(
    [FromQuery] Guid tenantId,
    [FromQuery] Guid? deviceId,
    [FromQuery] int take,
    CancellationToken cancellationToken)
  {
    if (!User.TryResolveTenantId(tenantId, out var resolvedTenantId))
    {
      return Forbid();
    }

    var query = _appDb.RemoteSessionLogs
      .AsNoTracking()
      .Where(x => x.TenantId == resolvedTenantId);

    if (deviceId is not null)
    {
      query = query.Where(x => x.DeviceId == deviceId);
    }

    var limit = take <= 0 ? DefaultTake : Math.Min(take, MaxTake);

    var logs = await query
      .OrderByDescending(x => x.StartedAt)
      .Take(limit)
      .Select(x => new V1Dtos.RemoteSessionLogDto(
        x.Id,
        x.SessionId,
        x.DeviceId,
        x.DeviceName,
        x.UserId,
        x.UserName,
        x.IpAddress,
        x.StartedAt,
        x.EndedAt,
        x.FailureReason,
        x.WasInterrupted))
      .ToArrayAsync(cancellationToken);

    return Ok(logs);
  }
}
