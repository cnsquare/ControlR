using ControlR.Libraries.Hosting;

namespace ControlR.Web.Server.Services.Scripting;

/// <summary>
/// Deletes script runs past the retention period. Their results go with them by cascade.
/// </summary>
public class ScriptRunCleanupBackgroundService(
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

  public async Task<int> CleanExpiredRuns(CancellationToken cancellationToken = default)
  {
    var retentionDays = _appOptions.Value.ScriptRunRetentionDays;
    if (retentionDays <= 0)
    {
      return 0;
    }

    var cutoff = _timeProvider.GetUtcNow() - TimeSpan.FromDays(retentionDays);

    await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
    var removedCount = await db.ScriptRuns
      .Where(x => x.CreatedAt < cutoff)
      .ExecuteDeleteCompatAsync(db, cancellationToken);

    if (removedCount > 0)
    {
      _logger.LogInformation("Removed {RemovedCount} script runs older than {Cutoff}.", removedCount, cutoff);
    }

    return removedCount;
  }

  protected override async Task HandleElapsed(CancellationToken stoppingToken)
  {
    await CleanExpiredRuns(stoppingToken);
  }

  protected override async Task OnStartingAsync(CancellationToken stoppingToken)
  {
    await CleanExpiredRuns(stoppingToken);
  }
}
