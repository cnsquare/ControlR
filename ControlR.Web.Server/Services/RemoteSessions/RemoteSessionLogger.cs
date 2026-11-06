using ControlR.Libraries.WebSocketRelay.Common.Sessions;

namespace ControlR.Web.Server.Services.RemoteSessions;

public interface IRemoteSessionLogger
{
  Task LogFailed(Guid sessionId, string reason);

  Task LogStarted(RemoteSessionStart session, CancellationToken cancellationToken);
}

public record RemoteSessionStart(
  Guid SessionId,
  Guid TenantId,
  Guid DeviceId,
  string DeviceName,
  Guid UserId,
  string UserName,
  string? IpAddress);

/// <summary>
/// Records who connected to which device. Runs outside the request that started a session,
/// because the session ends when the relay closes rather than in any hub call.
/// </summary>
public class RemoteSessionLogger(
  IDbContextFactory<AppDb> dbContextFactory,
  TimeProvider timeProvider)
  : IRemoteSessionLogger, IRelaySessionObserver
{
  private const int MaxFailureReasonLength = 512;
  private const string NeverConnectedReason = "The viewer and the device never connected.";

  private static readonly TimeSpan _endWriteTimeout = TimeSpan.FromSeconds(10);

  private readonly IDbContextFactory<AppDb> _dbContextFactory = dbContextFactory;
  private readonly TimeProvider _timeProvider = timeProvider;

  public Task LogFailed(Guid sessionId, string reason)
  {
    return CloseOpenLogs(sessionId, reason);
  }

  public async Task LogStarted(RemoteSessionStart session, CancellationToken cancellationToken)
  {
    await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

    db.RemoteSessionLogs.Add(new RemoteSessionLog
    {
      DeviceId = session.DeviceId,
      DeviceName = session.DeviceName,
      IpAddress = session.IpAddress,
      SessionId = session.SessionId,
      StartedAt = _timeProvider.GetUtcNow(),
      TenantId = session.TenantId,
      UserId = session.UserId,
      UserName = session.UserName,
    });

    await db.SaveChangesAsync(cancellationToken);
  }

  public Task OnPeerDisconnected(Guid sessionId, bool wasConnected)
  {
    return CloseOpenLogs(sessionId, wasConnected ? null : NeverConnectedReason);
  }

  private async Task CloseOpenLogs(Guid sessionId, string? failureReason)
  {
    // The caller's token is often already cancelled by the disconnect being recorded.
    using var cts = new CancellationTokenSource(_endWriteTimeout);

    await using var db = await _dbContextFactory.CreateDbContextAsync(cts.Token);

    // Every relay session passes through here, and terminal or file sessions have no log.
    var logs = await db.RemoteSessionLogs
      .Where(x => x.SessionId == sessionId && x.EndedAt == null)
      .ToListAsync(cts.Token);

    if (logs.Count == 0)
    {
      return;
    }

    var now = _timeProvider.GetUtcNow();
    foreach (var log in logs)
    {
      log.EndedAt = now;
      log.FailureReason = failureReason?[..Math.Min(failureReason.Length, MaxFailureReasonLength)];
      log.WasInterrupted = false;
    }

    await db.SaveChangesAsync(cts.Token);
  }
}
