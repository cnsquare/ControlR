using Asp.Versioning;
using ControlR.Web.Server.Constants;
using Microsoft.AspNetCore.Mvc;

namespace ControlR.Web.Server.Api.V1;

/// <summary>
/// Problems with scheduled scripts.
/// </summary>
[Route(HttpConstants.V1.ScriptAlertsEndpoint)]
[ApiController]
[Authorize]
[ApiVersion(ApiVersions.V1)]
public class ScriptAlertsController(AppDb appDb, TimeProvider timeProvider) : ControllerBase
{
  private readonly AppDb _appDb = appDb;
  private readonly TimeProvider _timeProvider = timeProvider;

  /// <summary>
  /// Dismisses an alert. The next failure of the same kind raises a new one.
  /// </summary>
  [HttpPost("{alertId:guid}/dismiss")]
  [Authorize(Policy = PolicyNames.RequireScriptsWrite)]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
  public async Task<IActionResult> DismissScriptAlert(
    [FromRoute] Guid alertId,
    [FromQuery] Guid tenantId,
    CancellationToken cancellationToken)
  {
    if (!User.TryResolveTenantId(tenantId, out var resolvedTenantId))
    {
      return Forbid();
    }

    var alert = await _appDb.ScriptAlerts
      .FirstOrDefaultAsync(x => x.Id == alertId && x.TenantId == resolvedTenantId, cancellationToken);

    if (alert is null)
    {
      return Problem(statusCode: StatusCodes.Status404NotFound, title: V1ProblemTitles.NotFound);
    }

    alert.DismissedAt = _timeProvider.GetUtcNow();
    alert.DismissedBy = User.Identity?.Name ?? "Unknown";
    await _appDb.SaveChangesAsync(cancellationToken);
    return NoContent();
  }

  /// <summary>
  /// Lists alerts that have not been dismissed, most recent first.
  /// </summary>
  [HttpGet]
  [Authorize(Policy = PolicyNames.RequireScriptsRead)]
  [ProducesResponseType(typeof(V1Dtos.ScriptAlertDto[]), StatusCodes.Status200OK)]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
  public async Task<ActionResult<V1Dtos.ScriptAlertDto[]>> GetScriptAlerts(
    [FromQuery] Guid tenantId,
    CancellationToken cancellationToken)
  {
    if (!User.TryResolveTenantId(tenantId, out var resolvedTenantId))
    {
      return Forbid();
    }

    var alerts = await _appDb.ScriptAlerts
      .AsNoTracking()
      .Where(x => x.TenantId == resolvedTenantId && x.DismissedAt == null)
      .OrderByDescending(x => x.LastOccurredAt)
      .Select(x => new V1Dtos.ScriptAlertDto(
        x.Id,
        x.ScheduleId,
        x.ScheduleName,
        x.DeviceId,
        x.DeviceName,
        x.Message,
        x.Occurrences,
        x.LastOccurredAt,
        x.ScriptRunId))
      .ToArrayAsync(cancellationToken);

    return Ok(alerts);
  }
}
