using ControlR.Libraries.Hosting;
using ControlR.Web.Server.Authz.Permissions;
using ControlR.Web.Server.Services.Authorization;

namespace ControlR.Web.Server.Services.Scripting;

/// <summary>
/// Runs schedules that have come due. A schedule acts on its creator's authority, so that
/// authority is checked again at every run rather than trusted from when it was created.
/// </summary>
public class ScriptScheduleBackgroundService(
  IServiceScopeFactory scopeFactory,
  TimeProvider timeProvider,
  ILogger<PeriodicBackgroundService> logger)
  : PeriodicBackgroundService(TimeSpan.FromMinutes(1), true, timeProvider, logger)
{
  private const int MaxSchedulesPerPass = 50;

  private readonly ILogger _logger = logger;
  private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
  private readonly TimeProvider _timeProvider = timeProvider;

  public static DateTimeOffset GetNextRun(
    DateTimeOffset previous,
    ScriptScheduleInterval interval,
    DateTimeOffset now)
  {
    // Runs missed while the server was down are skipped, not replayed one after another.
    var next = previous;
    do
    {
      next = interval switch
      {
        ScriptScheduleInterval.Hourly => next.AddHours(1),
        ScriptScheduleInterval.Daily => next.AddDays(1),
        ScriptScheduleInterval.Weekly => next.AddDays(7),
        ScriptScheduleInterval.Monthly => next.AddMonths(1),
        _ => throw new ArgumentOutOfRangeException(nameof(interval), interval, "Unknown interval."),
      };
    }
    while (next <= now);

    return next;
  }

  public async Task<int> RunDueSchedules(CancellationToken cancellationToken = default)
  {
    var now = _timeProvider.GetUtcNow();
    await using var scope = _scopeFactory.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();

    var due = await db.ScriptSchedules
      .Where(x => x.IsEnabled && x.NextRun <= now)
      .OrderBy(x => x.NextRun)
      .Take(MaxSchedulesPerPass)
      .ToListAsync(cancellationToken);

    foreach (var schedule in due)
    {
      try
      {
        await RunSchedule(scope.ServiceProvider, db, schedule, now, cancellationToken);

        if (schedule.LastError is { } error)
        {
          await scope.ServiceProvider
            .GetRequiredService<IScriptAlertManager>()
            .RaiseForSchedule(schedule, error, cancellationToken);
        }
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        _logger.LogError(ex, "Error while running script schedule {ScheduleId}.", schedule.Id);
      }
    }

    return due.Count;
  }

  protected override async Task HandleElapsed(CancellationToken stoppingToken)
  {
    await RunDueSchedules(stoppingToken);

    await using var scope = _scopeFactory.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<IScriptRunManager>().DispatchQueued(stoppingToken);
    await scope.ServiceProvider.GetRequiredService<IScriptAlertManager>().RaiseForFailedResults(stoppingToken);
  }

  private async Task RunSchedule(
    IServiceProvider services,
    AppDb db,
    ScriptSchedule schedule,
    DateTimeOffset now,
    CancellationToken cancellationToken)
  {
    // Saved before anything runs, so a crash mid-run cannot fire the same schedule twice.
    schedule.LastError = null;
    schedule.LastRun = now;
    schedule.NextRun = GetNextRun(schedule.NextRun, schedule.Interval, now);
    await db.SaveChangesAsync(cancellationToken);

    // Matching the tenant matters. An invite can move a user to another tenant entirely.
    var creator = await db.Users
      .AsNoTracking()
      .FirstOrDefaultAsync(
        x => x.Id == schedule.CreatedByUserId && x.TenantId == schedule.TenantId,
        cancellationToken);

    if (creator is null)
    {
      schedule.IsEnabled = false;
      schedule.LastError = "Disabled because its creator is no longer in this tenant.";
      await db.SaveChangesAsync(cancellationToken);
      _logger.LogWarning("Disabled script schedule {ScheduleId}. Its creator is gone.", schedule.Id);
      return;
    }

    var permitted = await GetPermittedDevices(services, db, schedule, creator, cancellationToken);
    var skipped = schedule.DeviceIds.Count - permitted.Count;

    if (permitted.Count == 0)
    {
      schedule.LastError = "Nothing ran because its creator can no longer run scripts on these devices.";
      await db.SaveChangesAsync(cancellationToken);
      return;
    }

    // A device away for a week must not come back to one queued copy per missed run.
    var devicesToRun = schedule.RunOnNextConnect
      ? await RemoveDevicesAlreadyWaiting(db, schedule, permitted, cancellationToken)
      : permitted;

    if (devicesToRun.Count > 0)
    {
      var run = await services.GetRequiredService<IScriptRunManager>().Start(
        new ScriptRunRequest(
          schedule.TenantId,
          $"Schedule \"{schedule.Name}\" ({schedule.CreatedBy})",
          creator.Id,
          devicesToRun,
          schedule.Shell,
          schedule.Content,
          schedule.TimeoutSeconds,
          schedule.RunOnNextConnect,
          schedule.Id),
        cancellationToken);

      schedule.LastRunId = run.Id;
    }

    if (skipped > 0)
    {
      schedule.LastError = $"Skipped {skipped} device(s) its creator can no longer reach.";
    }

    await db.SaveChangesAsync(cancellationToken);
  }

  private static async Task<List<Device>> RemoveDevicesAlreadyWaiting(
    AppDb db,
    ScriptSchedule schedule,
    List<Device> devices,
    CancellationToken cancellationToken)
  {
    var waiting = await db.ScriptResults
      .Where(x => x.Status == ScriptResultStatus.Queued && x.ScriptRun!.ScriptScheduleId == schedule.Id)
      .Select(x => x.DeviceId)
      .ToListAsync(cancellationToken);

    return [.. devices.Where(x => !waiting.Contains(x.Id))];
  }

  private static async Task<List<Device>> GetPermittedDevices(
    IServiceProvider services,
    AppDb db,
    ScriptSchedule schedule,
    AppUser creator,
    CancellationToken cancellationToken)
  {
    var evaluator = services.GetRequiredService<IPermissionEvaluator>();
    var resourceFactory = services.GetRequiredService<IResourceDescriptorFactory>();
    var principal = new PrincipalDescriptor(
      PrincipalType.User,
      creator.Id,
      creator.TenantId,
      AuthMethod: "scheduled-script-run");

    var devices = await db.Devices
      .AsNoTracking()
      .Where(x => x.TenantId == schedule.TenantId && schedule.DeviceIds.Contains(x.Id))
      .ToListAsync(cancellationToken);

    var permitted = new List<Device>();
    foreach (var device in devices)
    {
      var resource = await resourceFactory.CreateDevice(device, cancellationToken);
      var decision = await evaluator.Evaluate(
        principal,
        PermissionNames.DeviceScriptRun,
        resource,
        cancellationToken);

      if (decision.Allowed)
      {
        permitted.Add(device);
      }
    }

    return permitted;
  }
}
