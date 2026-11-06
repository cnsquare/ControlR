using System.Net;

namespace ControlR.Web.Server.Services.Scripting;

public interface IScriptAlertManager
{
  /// <summary>
  /// Raises alerts for scheduled results that have failed since the last pass. Every failure
  /// path ends in the same status, so reading the status here means none of them can be missed.
  /// </summary>
  Task<int> RaiseForFailedResults(CancellationToken cancellationToken);

  Task RaiseForSchedule(ScriptSchedule schedule, string message, CancellationToken cancellationToken);
}

public class ScriptAlertManager(
  AppDb appDb,
  IControlrEmailSender emailSender,
  TimeProvider timeProvider,
  ILogger<ScriptAlertManager> logger) : IScriptAlertManager
{
  private const int MaxResultsPerPass = 200;
  private const int MaxMessageLength = 512;

  private readonly AppDb _appDb = appDb;
  private readonly IControlrEmailSender _emailSender = emailSender;
  private readonly ILogger<ScriptAlertManager> _logger = logger;
  private readonly TimeProvider _timeProvider = timeProvider;

  public async Task<int> RaiseForFailedResults(CancellationToken cancellationToken)
  {
    var failed = await _appDb.ScriptResults
      .Include(x => x.Device)
      .Include(x => x.ScriptRun)
      .Where(x =>
        !x.IsAlertProcessed &&
        x.ScriptRun!.ScriptScheduleId != null &&
        (x.Status == ScriptResultStatus.Failed || x.Status == ScriptResultStatus.TimedOut))
      .Take(MaxResultsPerPass)
      .ToListAsync(cancellationToken);

    var raised = 0;
    foreach (var result in failed)
    {
      result.IsAlertProcessed = true;

      var schedule = await _appDb.ScriptSchedules.FirstOrDefaultAsync(
        x => x.Id == result.ScriptRun!.ScriptScheduleId && x.TenantId == result.TenantId,
        cancellationToken);

      // Someone paused or removed it, which is not a problem to report back to them.
      if (schedule is not { IsEnabled: true })
      {
        continue;
      }

      await Raise(schedule, result.Device, result.ScriptRunId, Describe(result), cancellationToken);
      raised++;
    }

    await _appDb.SaveChangesAsync(cancellationToken);
    return raised;
  }

  public async Task RaiseForSchedule(ScriptSchedule schedule, string message, CancellationToken cancellationToken)
  {
    await Raise(schedule, device: null, scriptRunId: schedule.LastRunId, message, cancellationToken);
    await _appDb.SaveChangesAsync(cancellationToken);
  }

  private static string Describe(ScriptResult result)
  {
    var message = result.Status == ScriptResultStatus.TimedOut
      ? "The script timed out."
      : result.FailureReason ?? $"The script failed with exit code {result.ExitCode}.";

    return message.Length <= MaxMessageLength ? message : message[..MaxMessageLength];
  }

  private async Task Raise(
    ScriptSchedule schedule,
    Device? device,
    Guid? scriptRunId,
    string message,
    CancellationToken cancellationToken)
  {
    var now = _timeProvider.GetUtcNow();
    var deviceId = device?.Id;

    var open = await _appDb.ScriptAlerts.FirstOrDefaultAsync(
      x => x.ScheduleId == schedule.Id &&
        x.TenantId == schedule.TenantId &&
        x.DeviceId == deviceId &&
        x.DismissedAt == null,
      cancellationToken);

    if (open is not null)
    {
      open.LastOccurredAt = now;
      open.Message = message;
      open.Occurrences++;
      open.ScriptRunId = scriptRunId;
      return;
    }

    _appDb.ScriptAlerts.Add(new ScriptAlert
    {
      Id = Guid.NewGuid(),
      CreatedAt = now,
      DeviceId = deviceId,
      DeviceName = device?.Name,
      LastOccurredAt = now,
      Message = message,
      ScheduleId = schedule.Id,
      ScheduleName = schedule.Name,
      ScriptRunId = scriptRunId,
      TenantId = schedule.TenantId,
    });

    await TrySendEmail(schedule, device, message);
  }

  private async Task TrySendEmail(ScriptSchedule schedule, Device? device, string message)
  {
    if (string.IsNullOrWhiteSpace(schedule.AlertEmail))
    {
      return;
    }

    // Script output stays out of email, since scripts print secrets. Everything shown is
    // encoded because a device chooses its own name.
    var deviceLine = device is null
      ? string.Empty
      : $"<p>Device: {WebUtility.HtmlEncode(device.Name)}</p>";

    var body =
      $"<p>Schedule: {WebUtility.HtmlEncode(schedule.Name)}</p>" +
      deviceLine +
      $"<p>{WebUtility.HtmlEncode(message)}</p>" +
      "<p>Further failures will not be emailed until this alert is dismissed in the Script Library.</p>";

    try
    {
      var result = await _emailSender.SendEmailWithResult(
        schedule.AlertEmail,
        "A scheduled script needs attention",
        body);

      if (!result.IsSuccess)
      {
        _logger.LogWarning("Failed to send a script alert email for schedule {ScheduleId}.", schedule.Id);
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error while sending a script alert email for schedule {ScheduleId}.", schedule.Id);
    }
  }
}
