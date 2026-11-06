using ControlR.Libraries.Hosting;

namespace ControlR.Web.Server.Services.RemoteSessions;

/// <summary>
/// Deletes session logs past the retention period. At startup it also flags sessions left open,
/// because the relay runs in this process and could not report their end.
/// </summary>
public class RemoteSessionLogCleanupBackgroundService(
  IDbContextFactory<AppDb> dbContextFactory,
  IOptions<AppOptions> appOptions,
  TimeProvider timeProvider,
  ILogger<PeriodicBackgroundService> logger)
  : PeriodicBackgroundService(TimeSpan.FromHours(24), true, timeProvider, logger)
{
  private readonly IOptions<AppOptions> _appOptions = appOptions;
  private readonly IDbContextFactory<AppDb> _dbContextFactory = dbContextFactory;
  private readonly ILogger _logger = logger;
  private readonly TimeProvider _timeProvider = timeProvider;

  public async Task<int> CleanExpiredLogs(CancellationToken cancellationToken = default)
  {
    var retentionDays = _appOptions.Value.RemoteSessionLogRetentionDays;
    if (retentionDays <= 0)
    {
      return 0;
    }

    var cutoff = _timeProvider.GetUtcNow() - TimeSpan.FromDays(retentionDays);

    await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
    var removedCount = await db.RemoteSessionLogs
      .Where(x => x.StartedAt < cutoff)
      .ExecuteDeleteCompatAsync(db, cancellationToken);

    if (removedCount > 0)
    {
      _logger.LogInformation("Removed {RemovedCount} remote session logs older than {Cutoff}.", removedCount, cutoff);
    }

    return removedCount;
  }

  public async Task<int> FlagInterruptedSessions(CancellationToken cancellationToken = default)
  {
    await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

    var openLogs = await db.RemoteSessionLogs
      .Where(x => x.EndedAt == null && !x.WasInterrupted)
      .ToListAsync(cancellationToken);

    foreach (var log in openLogs)
    {
      log.WasInterrupted = true;
    }

    await db.SaveChangesAsync(cancellationToken);
    return openLogs.Count;
  }

  protected override async Task HandleElapsed(CancellationToken stoppingToken)
  {
    await CleanExpiredLogs(stoppingToken);
  }

  protected override async Task OnStartingAsync(CancellationToken stoppingToken)
  {
    await FlagInterruptedSessions(stoppingToken);
    await CleanExpiredLogs(stoppingToken);
  }
}
