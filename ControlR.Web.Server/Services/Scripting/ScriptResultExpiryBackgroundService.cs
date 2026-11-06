using ControlR.Libraries.Hosting;

namespace ControlR.Web.Server.Services.Scripting;

/// <summary>
/// Fails results whose agent never reported, such as one that died mid-script, so
/// they do not stay pending forever. FinishedAt stays null, so a report that does
/// arrive later still replaces this outcome.
/// </summary>
public class ScriptResultExpiryBackgroundService(
  IDbContextFactory<AppDb> dbContextFactory,
  TimeProvider timeProvider,
  ILogger<PeriodicBackgroundService> logger)
  : PeriodicBackgroundService(
    period: TimeSpan.FromMinutes(1),
    catchExceptions: true,
    timeProvider: timeProvider,
    logger: logger)
{
  public const string ExpiredReason = "The agent did not report a result.";

  // Covers the agent's report retries on top of the script's own timeout.
  public static readonly TimeSpan GracePeriod = TimeSpan.FromMinutes(5);

  private readonly IDbContextFactory<AppDb> _dbContextFactory = dbContextFactory;
  private readonly ILogger _logger = logger;
  private readonly TimeProvider _timeProvider = timeProvider;

  public async Task<int> ExpireStaleResults(CancellationToken cancellationToken = default)
  {
    var cutoff = _timeProvider.GetUtcNow() - GracePeriod;

    await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

    var pending = await db.ScriptResults
      .Where(x => x.Status == ScriptResultStatus.Pending && x.ScriptRun!.CreatedAt < cutoff)
      .Select(x => new { Result = x, x.ScriptRun!.CreatedAt, x.ScriptRun.TimeoutSeconds })
      .ToListAsync(cancellationToken);

    var stale = pending
      .Where(x => x.CreatedAt.AddSeconds(x.TimeoutSeconds) < cutoff)
      .Select(x => x.Result)
      .ToList();

    if (stale.Count == 0)
    {
      return 0;
    }

    foreach (var result in stale)
    {
      result.FailureReason = ExpiredReason;
      result.Status = ScriptResultStatus.Failed;
    }

    await db.SaveChangesAsync(cancellationToken);

    _logger.LogWarning("Marked {ExpiredCount} script result(s) as failed after no report.", stale.Count);
    return stale.Count;
  }

  protected override async Task HandleElapsed(CancellationToken stoppingToken)
  {
    await ExpireStaleResults(stoppingToken);
  }
}
