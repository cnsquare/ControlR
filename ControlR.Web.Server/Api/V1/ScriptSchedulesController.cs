using Asp.Versioning;
using ControlR.Web.Server.Authz.Permissions;
using ControlR.Web.Server.Constants;
using ControlR.Web.Server.Services.Scripting;
using Microsoft.AspNetCore.Mvc;

namespace ControlR.Web.Server.Api.V1;

/// <summary>
/// Scheduled script runs. A schedule acts on its creator's authority, so creating or re-enabling
/// one needs the run permission on every device, as well as the library permission.
/// </summary>
[Route(HttpConstants.V1.ScriptSchedulesEndpoint)]
[ApiController]
[Authorize]
[ApiVersion(ApiVersions.V1)]
public class ScriptSchedulesController(
  AppDb appDb,
  IAuthorizationService authorizationService,
  TimeProvider timeProvider,
  ILogger<ScriptSchedulesController> logger) : ControllerBase
{
  private readonly AppDb _appDb = appDb;
  private readonly IAuthorizationService _authorizationService = authorizationService;
  private readonly ILogger<ScriptSchedulesController> _logger = logger;
  private readonly TimeProvider _timeProvider = timeProvider;

  [HttpPost]
  [Authorize(Policy = PolicyNames.RequireScriptsWrite)]
  [ProducesResponseType(typeof(V1Dtos.ScriptScheduleDto), StatusCodes.Status200OK)]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
  public async Task<ActionResult<V1Dtos.ScriptScheduleDto>> CreateScriptSchedule(
    [FromBody] V1Dtos.CreateScriptScheduleRequestDto request,
    [FromQuery] Guid tenantId,
    CancellationToken cancellationToken)
  {
    if (!User.TryResolveTenantId(tenantId, out var resolvedTenantId))
    {
      return Forbid();
    }

    // A schedule runs on a person's authority, which is what gets checked again at every run.
    if (User.ToPrincipalDescriptor() is not { PrincipalType: PrincipalType.User } actor)
    {
      return Forbid();
    }

    if (!Enum.IsDefined(request.Interval))
    {
      return Problem(
        detail: "Unknown schedule interval.",
        statusCode: StatusCodes.Status400BadRequest,
        title: V1ProblemTitles.InvalidRequest);
    }

    var script = await _appDb.SavedScripts
      .AsNoTracking()
      .FirstOrDefaultAsync(x => x.Id == request.SavedScriptId && x.TenantId == resolvedTenantId, cancellationToken);

    if (script is null)
    {
      return NotFoundProblem();
    }

    var deviceIds = request.DeviceIds.Distinct().ToList();
    var access = await CheckDeviceAccess(deviceIds, resolvedTenantId, cancellationToken);
    if (access is not null)
    {
      return access;
    }

    var schedule = new ScriptSchedule
    {
      Id = Guid.NewGuid(),
      AlertEmail = request.AlertEmail,
      Content = script.Content,
      CreatedBy = User.Identity?.Name ?? "Unknown",
      CreatedByUserId = actor.PrincipalId,
      DeviceIds = deviceIds,
      Interval = request.Interval,
      Name = request.Name,
      NextRun = request.StartAt ?? _timeProvider.GetUtcNow(),
      RunOnNextConnect = request.RunOnNextConnect,
      ScriptName = script.Name,
      Shell = script.Shell,
      TenantId = resolvedTenantId,
      TimeoutSeconds = script.TimeoutSeconds,
    };

    _appDb.ScriptSchedules.Add(schedule);
    await _appDb.SaveChangesAsync(cancellationToken);

    _logger.LogInformation(
      "User {UserName} created script schedule {ScheduleId} on {DeviceCount} device(s).",
      schedule.CreatedBy,
      schedule.Id,
      deviceIds.Count);

    return Ok(ToDto(schedule));
  }

  [HttpDelete("{scheduleId:guid}")]
  [Authorize(Policy = PolicyNames.RequireScriptsWrite)]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
  public async Task<IActionResult> DeleteScriptSchedule(
    [FromRoute] Guid scheduleId,
    [FromQuery] Guid tenantId,
    CancellationToken cancellationToken)
  {
    if (!User.TryResolveTenantId(tenantId, out var resolvedTenantId))
    {
      return Forbid();
    }

    var schedule = await _appDb.ScriptSchedules
      .FirstOrDefaultAsync(x => x.Id == scheduleId && x.TenantId == resolvedTenantId, cancellationToken);

    if (schedule is null)
    {
      return NotFoundProblem();
    }

    _appDb.ScriptSchedules.Remove(schedule);
    await _appDb.SaveChangesAsync(cancellationToken);
    return NoContent();
  }

  [HttpGet]
  [Authorize(Policy = PolicyNames.RequireScriptsRead)]
  [ProducesResponseType(typeof(V1Dtos.ScriptScheduleDto[]), StatusCodes.Status200OK)]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
  public async Task<ActionResult<V1Dtos.ScriptScheduleDto[]>> GetScriptSchedules(
    [FromQuery] Guid tenantId,
    CancellationToken cancellationToken)
  {
    if (!User.TryResolveTenantId(tenantId, out var resolvedTenantId))
    {
      return Forbid();
    }

    var schedules = await _appDb.ScriptSchedules
      .AsNoTracking()
      .Where(x => x.TenantId == resolvedTenantId)
      .OrderBy(x => x.Name)
      .ToListAsync(cancellationToken);

    return Ok(schedules.Select(ToDto).ToArray());
  }

  /// <summary>
  /// Pausing needs only the library permission. Resuming puts code back on a timer, so it needs
  /// the run permission on the schedule's devices too.
  /// </summary>
  [HttpPut("{scheduleId:guid}/enabled")]
  [Authorize(Policy = PolicyNames.RequireScriptsWrite)]
  [ProducesResponseType(typeof(V1Dtos.ScriptScheduleDto), StatusCodes.Status200OK)]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
  public async Task<ActionResult<V1Dtos.ScriptScheduleDto>> SetScriptScheduleEnabled(
    [FromRoute] Guid scheduleId,
    [FromBody] V1Dtos.SetScriptScheduleEnabledRequestDto request,
    [FromQuery] Guid tenantId,
    CancellationToken cancellationToken)
  {
    if (!User.TryResolveTenantId(tenantId, out var resolvedTenantId))
    {
      return Forbid();
    }

    var schedule = await _appDb.ScriptSchedules
      .FirstOrDefaultAsync(x => x.Id == scheduleId && x.TenantId == resolvedTenantId, cancellationToken);

    if (schedule is null)
    {
      return NotFoundProblem();
    }

    if (request.IsEnabled && !schedule.IsEnabled)
    {
      var access = await CheckDeviceAccess(schedule.DeviceIds, resolvedTenantId, cancellationToken);
      if (access is not null)
      {
        return access;
      }

      // A resumed schedule waits for its next slot rather than firing for the time it was paused.
      var now = _timeProvider.GetUtcNow();
      if (schedule.NextRun <= now)
      {
        schedule.NextRun = ScriptScheduleBackgroundService.GetNextRun(schedule.NextRun, schedule.Interval, now);
      }
    }

    schedule.IsEnabled = request.IsEnabled;
    await _appDb.SaveChangesAsync(cancellationToken);
    return Ok(ToDto(schedule));
  }

  private static V1Dtos.ScriptScheduleDto ToDto(ScriptSchedule schedule)
  {
    return new V1Dtos.ScriptScheduleDto(
      schedule.Id,
      schedule.Name,
      schedule.ScriptName,
      schedule.Shell,
      schedule.Interval,
      [.. schedule.DeviceIds],
      schedule.IsEnabled,
      schedule.NextRun,
      schedule.LastRun,
      schedule.LastRunId,
      schedule.LastError,
      schedule.CreatedBy,
      schedule.RunOnNextConnect,
      schedule.AlertEmail);
  }

  private async Task<ActionResult?> CheckDeviceAccess(
    IReadOnlyCollection<Guid> deviceIds,
    Guid tenantId,
    CancellationToken cancellationToken)
  {
    var devices = await _appDb.Devices
      .AsNoTracking()
      .Where(x => x.TenantId == tenantId && deviceIds.Contains(x.Id))
      .ToListAsync(cancellationToken);

    if (devices.Count != deviceIds.Count)
    {
      return NotFoundProblem();
    }

    foreach (var device in devices)
    {
      var authResult = await _authorizationService.AuthorizeAsync(User, device, DeviceResourcePolicies.ScriptRun);
      if (!authResult.Succeeded)
      {
        return Forbid();
      }
    }

    return null;
  }

  private ObjectResult NotFoundProblem()
  {
    return Problem(statusCode: StatusCodes.Status404NotFound, title: V1ProblemTitles.NotFound);
  }
}
