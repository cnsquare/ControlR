using Asp.Versioning;
using ControlR.Web.Server.Authz.Permissions;
using ControlR.Web.Server.Constants;
using ControlR.Web.Server.Services.Scripting;
using Microsoft.AspNetCore.Mvc;

namespace ControlR.Web.Server.Api.V1;

/// <summary>
/// Runs scripts on devices and reads back their results.
/// </summary>
[Route(HttpConstants.V1.ScriptRunsEndpoint)]
[ApiController]
[Authorize]
[ApiVersion(ApiVersions.V1)]
public class ScriptRunsController(
  AppDb appDb,
  IScriptRunManager scriptRunManager,
  IAuthorizationService authorizationService,
  ILogger<ScriptRunsController> logger) : ControllerBase
{
  private readonly AppDb _appDb = appDb;
  private readonly IAuthorizationService _authorizationService = authorizationService;
  private readonly ILogger<ScriptRunsController> _logger = logger;
  private readonly IScriptRunManager _scriptRunManager = scriptRunManager;

  /// <summary>
  /// Starts a script on every named device. The caller must hold the run permission on all of
  /// them, or nothing runs. Each device's outcome is reported in its own result.
  /// </summary>
  [HttpPost]
  [ProducesResponseType(typeof(V1Dtos.ScriptRunDto), StatusCodes.Status200OK)]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
  public async Task<ActionResult<V1Dtos.ScriptRunDto>> CreateScriptRun(
    [FromBody] V1Dtos.CreateScriptRunRequestDto request,
    [FromQuery] Guid tenantId,
    CancellationToken cancellationToken)
  {
    if (!User.TryResolveTenantId(tenantId, out var resolvedTenantId))
    {
      return Forbid();
    }

    if (!Enum.IsDefined(request.Shell))
    {
      return Problem(
        detail: "Unknown script shell.",
        statusCode: StatusCodes.Status400BadRequest,
        title: V1ProblemTitles.InvalidRequest);
    }

    var deviceIds = request.DeviceIds.Distinct().ToArray();

    var devices = await _appDb.Devices
      .AsNoTracking()
      .Where(x => x.TenantId == resolvedTenantId && deviceIds.Contains(x.Id))
      .ToListAsync(cancellationToken);

    if (devices.Count != deviceIds.Length)
    {
      return Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: V1ProblemTitles.NotFound);
    }

    foreach (var device in devices)
    {
      var authResult = await _authorizationService.AuthorizeAsync(User, device, DeviceResourcePolicies.ScriptRun);
      if (!authResult.Succeeded)
      {
        _logger.LogCritical(
          "Script run rejected. User {UserName} lacks the permission on device {DeviceId}.",
          User.Identity?.Name,
          device.Id);
        return Forbid();
      }
    }

    var actor = User.ToPrincipalDescriptor();

    // A queued result is checked against a person's access when its device connects.
    if (request.RunOnNextConnect && actor?.PrincipalType != PrincipalType.User)
    {
      return Problem(
        detail: "Only a user can queue a script for when a device connects.",
        statusCode: StatusCodes.Status400BadRequest,
        title: V1ProblemTitles.InvalidRequest);
    }

    var initiatedBy = User.Identity?.Name ?? "Unknown";

    _logger.LogInformation(
      "User {UserName} is running a {Shell} script on {DeviceCount} device(s).",
      initiatedBy,
      request.Shell,
      devices.Count);

    var run = await _scriptRunManager.Start(
      new ScriptRunRequest(
        resolvedTenantId,
        initiatedBy,
        actor?.PrincipalType == PrincipalType.User ? actor.PrincipalId : null,
        devices,
        request.Shell,
        request.Content,
        request.TimeoutSeconds,
        request.RunOnNextConnect),
      cancellationToken);

    return Ok(ToDto(run, run.Results ?? []));
  }

  /// <summary>
  /// Cancels the parts of a run still waiting for a device, on devices the caller may run scripts on.
  /// </summary>
  [HttpPost("{scriptRunId:guid}/cancel")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
  public async Task<IActionResult> CancelQueuedScriptRun(
    [FromRoute] Guid scriptRunId,
    [FromQuery] Guid tenantId,
    CancellationToken cancellationToken)
  {
    if (!User.TryResolveTenantId(tenantId, out var resolvedTenantId))
    {
      return Forbid();
    }

    var queued = await _appDb.ScriptResults
      .Include(x => x.Device)
      .Where(x =>
        x.ScriptRunId == scriptRunId &&
        x.TenantId == resolvedTenantId &&
        x.Status == ScriptResultStatus.Queued)
      .ToListAsync(cancellationToken);

    var cancelled = 0;
    foreach (var result in queued)
    {
      var authResult = await _authorizationService.AuthorizeAsync(User, result.Device!, DeviceResourcePolicies.ScriptRun);
      if (authResult.Succeeded)
      {
        result.FailureReason = $"Cancelled by {User.Identity?.Name ?? "a user"} before it ran.";
        result.IsAlertProcessed = true;
        result.Status = ScriptResultStatus.Failed;
        cancelled++;
      }
    }

    if (cancelled == 0)
    {
      return Problem(statusCode: StatusCodes.Status404NotFound, title: V1ProblemTitles.NotFound);
    }

    await _appDb.SaveChangesAsync(cancellationToken);
    return NoContent();
  }

  /// <summary>
  /// Lists a device's script runs, newest first. Output is left out, so read a run for its detail.
  /// </summary>
  [HttpGet]
  [ProducesResponseType(typeof(V1Dtos.ScriptRunHistoryItemDto[]), StatusCodes.Status200OK)]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
  public async Task<ActionResult<V1Dtos.ScriptRunHistoryItemDto[]>> GetScriptRunHistory(
    [FromQuery] Guid deviceId,
    [FromQuery] Guid tenantId,
    [FromQuery] int limit = ScriptLimits.HistoryDefaultCount,
    CancellationToken cancellationToken = default)
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

    var authResult = await _authorizationService.AuthorizeAsync(User, device, DeviceResourcePolicies.ScriptRun);
    if (!authResult.Succeeded)
    {
      return Forbid();
    }

    var history = await _appDb.ScriptResults
      .AsNoTracking()
      .Where(x => x.DeviceId == deviceId && x.TenantId == resolvedTenantId)
      .OrderByDescending(x => x.ScriptRun!.CreatedAt)
      .Take(Math.Clamp(limit, 1, ScriptLimits.HistoryMaxCount))
      .Select(x => new V1Dtos.ScriptRunHistoryItemDto(
        x.ScriptRunId,
        x.ScriptRun!.Shell,
        x.ScriptRun.Content.Length > ScriptLimits.ContentPreviewLength
          ? x.ScriptRun.Content.Substring(0, ScriptLimits.ContentPreviewLength)
          : x.ScriptRun.Content,
        x.ScriptRun.InitiatedBy,
        x.ScriptRun.CreatedAt,
        x.Status,
        x.ExitCode))
      .ToArrayAsync(cancellationToken);

    return Ok(history);
  }

  /// <summary>
  /// Reads a script run. Results are limited to the devices the caller may run scripts on.
  /// </summary>
  [HttpGet("{scriptRunId:guid}")]
  [ProducesResponseType(typeof(V1Dtos.ScriptRunDto), StatusCodes.Status200OK)]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
  public async Task<ActionResult<V1Dtos.ScriptRunDto>> GetScriptRun(
    [FromRoute] Guid scriptRunId,
    [FromQuery] Guid tenantId,
    CancellationToken cancellationToken)
  {
    if (!User.TryResolveTenantId(tenantId, out var resolvedTenantId))
    {
      return Forbid();
    }

    var run = await _appDb.ScriptRuns
      .AsNoTracking()
      .Include(x => x.Results!)
      .ThenInclude(x => x.Device)
      .FirstOrDefaultAsync(x => x.Id == scriptRunId && x.TenantId == resolvedTenantId, cancellationToken);

    if (run is null)
    {
      return Problem(statusCode: StatusCodes.Status404NotFound, title: V1ProblemTitles.NotFound);
    }

    var visibleResults = new List<ScriptResult>();
    foreach (var result in run.Results ?? [])
    {
      if (result.Device is null)
      {
        continue;
      }

      var authResult = await _authorizationService.AuthorizeAsync(User, result.Device, DeviceResourcePolicies.ScriptRun);
      if (authResult.Succeeded)
      {
        visibleResults.Add(result);
      }
    }

    // The same answer as a run that does not exist, so its existence is not disclosed.
    if (visibleResults.Count == 0)
    {
      return Problem(statusCode: StatusCodes.Status404NotFound, title: V1ProblemTitles.NotFound);
    }

    return Ok(ToDto(run, visibleResults));
  }

  private static V1Dtos.ScriptRunDto ToDto(ScriptRun run, IEnumerable<ScriptResult> results)
  {
    return new V1Dtos.ScriptRunDto(
      run.Id,
      run.Shell,
      run.Content,
      run.TimeoutSeconds,
      run.InitiatedBy,
      run.CreatedAt,
      [
        .. results.Select(x => new V1Dtos.ScriptResultDto(
          x.Id,
          x.DeviceId,
          x.Status,
          x.ExitCode,
          x.StandardOutput,
          x.StandardError,
          x.OutputTruncated,
          x.FailureReason,
          x.StartedAt,
          x.FinishedAt))
      ]);
  }
}
