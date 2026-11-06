using System.Net.Mail;
using Asp.Versioning;
using ControlR.Web.Server.Constants;
using ControlR.Web.Server.Services.Alerts;
using Microsoft.AspNetCore.Mvc;

namespace ControlR.Web.Server.Api.V1;

/// <summary>
/// Device health alerts and where they are sent. Reading the settings needs the write permission,
/// because a webhook URL usually contains a secret.
/// </summary>
[Route(HttpConstants.V1.DeviceAlertsEndpoint)]
[ApiController]
[Authorize]
[ApiVersion(ApiVersions.V1)]
public class DeviceAlertsController(
  AppDb appDb,
  IAlertNotifier alertNotifier,
  IOptionsMonitor<AppOptions> appOptions,
  TimeProvider timeProvider) : ControllerBase
{
  private const int MaxLowDiskPercent = 99;
  private const int MaxOfflineMinutes = 60 * 24 * 30;

  private readonly IAlertNotifier _alertNotifier = alertNotifier;
  private readonly AppDb _appDb = appDb;
  private readonly IOptionsMonitor<AppOptions> _appOptions = appOptions;
  private readonly TimeProvider _timeProvider = timeProvider;

  /// <summary>
  /// Hides an alert. It is not raised again until the condition clears and comes back.
  /// </summary>
  [HttpPost("{alertId:guid}/dismiss")]
  [Authorize(Policy = PolicyNames.RequireTenantSettingsWrite)]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
  public async Task<IActionResult> DismissDeviceAlert(
    [FromRoute] Guid alertId,
    [FromQuery] Guid tenantId,
    CancellationToken cancellationToken)
  {
    if (!User.TryResolveTenantId(tenantId, out var resolvedTenantId))
    {
      return Forbid();
    }

    var alert = await _appDb.DeviceAlerts
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
  /// Lists alerts that are still active and have not been dismissed, most recent first.
  /// </summary>
  [HttpGet]
  [Authorize(Policy = PolicyNames.RequireTenantSettingsRead)]
  [ProducesResponseType(typeof(V1Dtos.DeviceAlertDto[]), StatusCodes.Status200OK)]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
  public async Task<ActionResult<V1Dtos.DeviceAlertDto[]>> GetDeviceAlerts(
    [FromQuery] Guid tenantId,
    CancellationToken cancellationToken)
  {
    if (!User.TryResolveTenantId(tenantId, out var resolvedTenantId))
    {
      return Forbid();
    }

    var alerts = await _appDb.DeviceAlerts
      .AsNoTracking()
      .Where(x => x.TenantId == resolvedTenantId && x.ResolvedAt == null && x.DismissedAt == null)
      .OrderByDescending(x => x.RaisedAt)
      .Select(x => new V1Dtos.DeviceAlertDto(x.Id, x.DeviceId, x.DeviceName, x.Kind, x.Message, x.RaisedAt))
      .ToArrayAsync(cancellationToken);

    return Ok(alerts);
  }

  [HttpGet("settings")]
  [Authorize(Policy = PolicyNames.RequireTenantSettingsWrite)]
  [ProducesResponseType(typeof(V1Dtos.AlertSettingsDto), StatusCodes.Status200OK)]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
  public async Task<ActionResult<V1Dtos.AlertSettingsDto>> GetAlertSettings(
    [FromQuery] Guid tenantId,
    CancellationToken cancellationToken)
  {
    if (!User.TryResolveTenantId(tenantId, out var resolvedTenantId))
    {
      return Forbid();
    }

    var settings = await FindSettings(resolvedTenantId, cancellationToken);
    return Ok(settings is null
      ? new V1Dtos.AlertSettingsDto(0, 0, null, null)
      : new V1Dtos.AlertSettingsDto(settings.OfflineMinutes, settings.LowDiskPercent, settings.Email, settings.WebhookUrl));
  }

  /// <summary>
  /// Sends a test message to the saved email address and webhook.
  /// </summary>
  [HttpPost("settings/test")]
  [Authorize(Policy = PolicyNames.RequireTenantSettingsWrite)]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway, "application/problem+json")]
  public async Task<IActionResult> TestAlertSettings(
    [FromQuery] Guid tenantId,
    CancellationToken cancellationToken)
  {
    if (!User.TryResolveTenantId(tenantId, out var resolvedTenantId))
    {
      return Forbid();
    }

    var settings = await FindSettings(resolvedTenantId, cancellationToken);
    if (settings is null ||
        (string.IsNullOrWhiteSpace(settings.Email) && string.IsNullOrWhiteSpace(settings.WebhookUrl)))
    {
      return InvalidRequest("Save an email address or webhook URL first.");
    }

    var succeeded = await _alertNotifier.Notify(
      settings,
      "ControlR test alert",
      ["Alerts from ControlR will arrive here."],
      cancellationToken);

    return succeeded
      ? NoContent()
      : Problem(
        detail: "The test could not be delivered.",
        statusCode: StatusCodes.Status502BadGateway,
        title: V1ProblemTitles.InvalidRequest);
  }

  [HttpPut("settings")]
  [Authorize(Policy = PolicyNames.RequireTenantSettingsWrite)]
  [ProducesResponseType(typeof(V1Dtos.AlertSettingsDto), StatusCodes.Status200OK)]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
  public async Task<ActionResult<V1Dtos.AlertSettingsDto>> UpdateAlertSettings(
    [FromBody] V1Dtos.AlertSettingsDto request,
    [FromQuery] Guid tenantId,
    CancellationToken cancellationToken)
  {
    if (!User.TryResolveTenantId(tenantId, out var resolvedTenantId))
    {
      return Forbid();
    }

    if (request.OfflineMinutes is < 0 or > MaxOfflineMinutes)
    {
      return InvalidRequest($"Offline minutes must be between 0 and {MaxOfflineMinutes}.");
    }

    if (request.LowDiskPercent is < 0 or > MaxLowDiskPercent)
    {
      return InvalidRequest($"Low disk percent must be between 0 and {MaxLowDiskPercent}.");
    }

    var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
    if (email is not null && (email.Length > 256 || !MailAddress.TryCreate(email, out _)))
    {
      return InvalidRequest("The email address is not valid.");
    }

    var webhookUrl = string.IsNullOrWhiteSpace(request.WebhookUrl) ? null : request.WebhookUrl.Trim();
    if (webhookUrl is not null &&
        (webhookUrl.Length > 2048 ||
         !WebhookTargetPolicy.IsAllowedUrl(webhookUrl, _appOptions.CurrentValue.AllowPrivateWebhookTargets)))
    {
      return InvalidRequest("The webhook must be an https URL on a public address.");
    }

    var settings = await FindSettings(resolvedTenantId, cancellationToken);
    if (settings is null)
    {
      settings = new AlertSettings { Id = Guid.NewGuid(), TenantId = resolvedTenantId };
      _appDb.AlertSettings.Add(settings);
    }

    settings.Email = email;
    settings.LowDiskPercent = request.LowDiskPercent;
    settings.OfflineMinutes = request.OfflineMinutes;
    settings.WebhookUrl = webhookUrl;

    await _appDb.SaveChangesAsync(cancellationToken);
    return Ok(new V1Dtos.AlertSettingsDto(settings.OfflineMinutes, settings.LowDiskPercent, settings.Email, settings.WebhookUrl));
  }

  private Task<AlertSettings?> FindSettings(Guid tenantId, CancellationToken cancellationToken)
  {
    return _appDb.AlertSettings.FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);
  }

  private ObjectResult InvalidRequest(string detail)
  {
    return Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest, title: V1ProblemTitles.InvalidRequest);
  }
}
