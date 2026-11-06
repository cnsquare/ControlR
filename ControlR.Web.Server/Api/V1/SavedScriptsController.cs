using Asp.Versioning;
using ControlR.Web.Server.Constants;
using Microsoft.AspNetCore.Mvc;

namespace ControlR.Web.Server.Api.V1;

/// <summary>
/// The tenant's library of reusable scripts. Saving a script runs nothing, so these operations
/// need only the library permissions. Running one needs the device permission as usual.
/// </summary>
[Route(HttpConstants.V1.SavedScriptsEndpoint)]
[ApiController]
[Authorize]
[ApiVersion(ApiVersions.V1)]
public class SavedScriptsController(AppDb appDb) : ControllerBase
{
  private readonly AppDb _appDb = appDb;

  [HttpPost]
  [Authorize(Policy = PolicyNames.RequireScriptsWrite)]
  [ProducesResponseType(typeof(V1Dtos.SavedScriptDto), StatusCodes.Status200OK)]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
  public async Task<ActionResult<V1Dtos.SavedScriptDto>> CreateSavedScript(
    [FromBody] V1Dtos.SaveSavedScriptRequestDto request,
    [FromQuery] Guid tenantId,
    CancellationToken cancellationToken)
  {
    if (!User.TryResolveTenantId(tenantId, out var resolvedTenantId))
    {
      return Forbid();
    }

    if (ValidateShell(request) is { } invalid)
    {
      return invalid;
    }

    if (await IsNameTaken(request.Name, resolvedTenantId, excludeId: null, cancellationToken))
    {
      return NameConflict();
    }

    var script = new SavedScript
    {
      Id = Guid.NewGuid(),
      Content = request.Content,
      CreatedBy = User.Identity?.Name ?? "Unknown",
      Name = request.Name,
      Shell = request.Shell,
      TenantId = resolvedTenantId,
      TimeoutSeconds = request.TimeoutSeconds,
    };

    _appDb.SavedScripts.Add(script);
    await _appDb.SaveChangesAsync(cancellationToken);
    return Ok(ToDto(script));
  }

  [HttpDelete("{savedScriptId:guid}")]
  [Authorize(Policy = PolicyNames.RequireScriptsWrite)]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
  public async Task<IActionResult> DeleteSavedScript(
    [FromRoute] Guid savedScriptId,
    [FromQuery] Guid tenantId,
    CancellationToken cancellationToken)
  {
    if (!User.TryResolveTenantId(tenantId, out var resolvedTenantId))
    {
      return Forbid();
    }

    var script = await FindScript(savedScriptId, resolvedTenantId, cancellationToken);
    if (script is null)
    {
      return NotFoundProblem();
    }

    _appDb.SavedScripts.Remove(script);
    await _appDb.SaveChangesAsync(cancellationToken);
    return NoContent();
  }

  [HttpGet]
  [Authorize(Policy = PolicyNames.RequireScriptsRead)]
  [ProducesResponseType(typeof(V1Dtos.SavedScriptDto[]), StatusCodes.Status200OK)]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
  public async Task<ActionResult<V1Dtos.SavedScriptDto[]>> GetSavedScripts(
    [FromQuery] Guid tenantId,
    CancellationToken cancellationToken)
  {
    if (!User.TryResolveTenantId(tenantId, out var resolvedTenantId))
    {
      return Forbid();
    }

    var scripts = await _appDb.SavedScripts
      .AsNoTracking()
      .Where(x => x.TenantId == resolvedTenantId)
      .OrderBy(x => x.Name)
      .ToListAsync(cancellationToken);

    return Ok(scripts.Select(ToDto).ToArray());
  }

  [HttpPut("{savedScriptId:guid}")]
  [Authorize(Policy = PolicyNames.RequireScriptsWrite)]
  [ProducesResponseType(typeof(V1Dtos.SavedScriptDto), StatusCodes.Status200OK)]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
  public async Task<ActionResult<V1Dtos.SavedScriptDto>> UpdateSavedScript(
    [FromRoute] Guid savedScriptId,
    [FromBody] V1Dtos.SaveSavedScriptRequestDto request,
    [FromQuery] Guid tenantId,
    CancellationToken cancellationToken)
  {
    if (!User.TryResolveTenantId(tenantId, out var resolvedTenantId))
    {
      return Forbid();
    }

    if (ValidateShell(request) is { } invalid)
    {
      return invalid;
    }

    var script = await FindScript(savedScriptId, resolvedTenantId, cancellationToken);
    if (script is null)
    {
      return NotFoundProblem();
    }

    if (await IsNameTaken(request.Name, resolvedTenantId, savedScriptId, cancellationToken))
    {
      return NameConflict();
    }

    script.Content = request.Content;
    script.Name = request.Name;
    script.Shell = request.Shell;
    script.TimeoutSeconds = request.TimeoutSeconds;

    await _appDb.SaveChangesAsync(cancellationToken);
    return Ok(ToDto(script));
  }

  private static V1Dtos.SavedScriptDto ToDto(SavedScript script)
  {
    return new V1Dtos.SavedScriptDto(
      script.Id,
      script.Name,
      script.Shell,
      script.Content,
      script.TimeoutSeconds,
      script.CreatedBy,
      script.CreatedAt);
  }

  private Task<SavedScript?> FindScript(Guid id, Guid tenantId, CancellationToken cancellationToken)
  {
    return _appDb.SavedScripts.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, cancellationToken);
  }

  private Task<bool> IsNameTaken(string name, Guid tenantId, Guid? excludeId, CancellationToken cancellationToken)
  {
    return _appDb.SavedScripts.AnyAsync(
      x => x.TenantId == tenantId && x.Name == name && x.Id != excludeId,
      cancellationToken);
  }

  private ObjectResult NameConflict()
  {
    return Problem(
      detail: "A saved script with this name already exists.",
      statusCode: StatusCodes.Status409Conflict,
      title: V1ProblemTitles.Conflict);
  }

  private ObjectResult NotFoundProblem()
  {
    return Problem(statusCode: StatusCodes.Status404NotFound, title: V1ProblemTitles.NotFound);
  }

  private ObjectResult? ValidateShell(V1Dtos.SaveSavedScriptRequestDto request)
  {
    return Enum.IsDefined(request.Shell)
      ? null
      : Problem(
        detail: "Unknown script shell.",
        statusCode: StatusCodes.Status400BadRequest,
        title: V1ProblemTitles.InvalidRequest);
  }
}
