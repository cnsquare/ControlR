using Asp.Versioning;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1.Recordings;
using ControlR.Web.Server.Services.Recordings;
using Microsoft.AspNetCore.Mvc;

namespace ControlR.Web.Server.Api.V1;

/// <summary>
/// Session recordings: upload, list, stream/download, and delete. Tenant-scoped by the caller's
/// tenant claim. Files live on the filesystem (see <see cref="IRecordingStorage"/>).
/// </summary>
[Route(HttpConstants.V1.RecordingsEndpoint)]
[ApiController]
[Authorize]
[ApiVersion(ApiVersions.V1)]
public class RecordingsController : ControllerBase
{
  // Recordings can be large; allow a generous multipart body for the upload action.
  [HttpPost]
  [RequestSizeLimit(2_147_483_648)]
  [RequestFormLimits(MultipartBodyLengthLimit = 2_147_483_648)]
  [ProducesResponseType<RecordingResponseDto>(StatusCodes.Status201Created)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<RecordingResponseDto>> Upload(
    [FromServices] AppDb appDb,
    [FromServices] IRecordingStorage storage,
    [FromForm] IFormFile file,
    [FromForm] Guid? deviceId,
    [FromForm] string? deviceName,
    [FromForm] int? durationSeconds,
    CancellationToken cancellationToken)
  {
    if (!User.TryGetTenantId(out var tenantId))
    {
      return Unauthorized();
    }

    if (file is null || file.Length == 0)
    {
      return Problem(detail: "No file was provided.", statusCode: StatusCodes.Status400BadRequest);
    }

    var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "video/webm" : file.ContentType;
    var extension = contentType.Contains("mp4", StringComparison.OrdinalIgnoreCase) ? "mp4" : "webm";
    var storedName = $"{Guid.NewGuid():N}.{extension}";

    long size;
    await using (var stream = file.OpenReadStream())
    {
      size = await storage.Save(tenantId, storedName, stream, cancellationToken);
    }

    User.TryGetUserId(out var userId);

    var recording = new Recording
    {
      TenantId = tenantId,
      DeviceId = deviceId,
      DeviceName = deviceName,
      CreatedByUserId = userId == Guid.Empty ? null : userId,
      CreatedByUserName = User.Identity?.Name,
      FileName = storedName,
      ContentType = contentType,
      SizeBytes = size,
      DurationSeconds = durationSeconds,
    };

    appDb.Recordings.Add(recording);
    await appDb.SaveChangesAsync(cancellationToken);

    return CreatedAtAction(nameof(GetContent), new { id = recording.Id }, ToDto(recording));
  }

  [HttpGet]
  [Authorize(Policy = PolicyNames.RequireRecordingsRead)]
  [ProducesResponseType<IReadOnlyList<RecordingResponseDto>>(StatusCodes.Status200OK)]
  public async Task<ActionResult<IReadOnlyList<RecordingResponseDto>>> GetAll(
    [FromServices] AppDb appDb,
    CancellationToken cancellationToken)
  {
    if (!User.TryGetTenantId(out var tenantId))
    {
      return Unauthorized();
    }

    var recordings = await appDb.Recordings
      .AsNoTracking()
      .Where(x => x.TenantId == tenantId)
      .OrderByDescending(x => x.CreatedAt)
      .Select(x => ToDto(x))
      .ToListAsync(cancellationToken);

    return Ok(recordings);
  }

  [HttpGet("{id:guid}/content")]
  [Authorize(Policy = PolicyNames.RequireRecordingsRead)]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> GetContent(
    [FromServices] AppDb appDb,
    [FromServices] IRecordingStorage storage,
    [FromRoute] Guid id,
    [FromQuery] bool download,
    CancellationToken cancellationToken)
  {
    if (!User.TryGetTenantId(out var tenantId))
    {
      return Unauthorized();
    }

    var recording = await appDb.Recordings
      .AsNoTracking()
      .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, cancellationToken);

    if (recording is null || !storage.TryGetPath(tenantId, recording.FileName, out var path))
    {
      return NotFound();
    }

    // enableRangeProcessing lets the browser seek within the video while watching.
    var downloadName = download ? BuildDownloadName(recording) : null;
    return PhysicalFile(path, recording.ContentType, downloadName, enableRangeProcessing: true);
  }

  [HttpDelete("{id:guid}")]
  [Authorize(Policy = PolicyNames.RequireRecordingsDelete)]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> Delete(
    [FromServices] AppDb appDb,
    [FromServices] IRecordingStorage storage,
    [FromRoute] Guid id,
    CancellationToken cancellationToken)
  {
    if (!User.TryGetTenantId(out var tenantId))
    {
      return Unauthorized();
    }

    var recording = await appDb.Recordings
      .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, cancellationToken);

    if (recording is null)
    {
      return NotFound();
    }

    storage.Delete(tenantId, recording.FileName);
    appDb.Recordings.Remove(recording);
    await appDb.SaveChangesAsync(cancellationToken);

    return NoContent();
  }

  private static string BuildDownloadName(Recording recording)
  {
    var extension = recording.ContentType.Contains("mp4", StringComparison.OrdinalIgnoreCase) ? "mp4" : "webm";
    var device = string.IsNullOrWhiteSpace(recording.DeviceName) ? "session" : recording.DeviceName;
    var stamp = recording.CreatedAt.ToString("yyyy-MM-dd-HHmmss");
    var safeDevice = string.Join("_", device.Split(Path.GetInvalidFileNameChars()));
    return $"{safeDevice}-{stamp}.{extension}";
  }

  private static RecordingResponseDto ToDto(Recording x) => new(
    x.Id,
    x.DeviceId,
    x.DeviceName,
    x.CreatedByUserName,
    x.ContentType,
    x.SizeBytes,
    x.DurationSeconds,
    x.CreatedAt);
}
