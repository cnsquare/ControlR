using System.Security.Claims;
using ControlR.Libraries.WebSocketRelay.Common.Sessions;

namespace ControlR.Web.Server.Services.RemoteSessions;

public interface IRemoteSessionLogger
{
  Task LogEnded(Guid sessionId);

  Task LogFailed(Guid sessionId, string reason);

  /// <summary>
  /// Records a file operation, which has no duration of its own. Runs before the agent is asked,
  /// so a failed write stops the operation.
  /// </summary>
  Task LogFileOperation(Device device, ClaimsPrincipal user, string detail, CancellationToken cancellationToken);

  Task LogStarted(RemoteSessionStart session, CancellationToken cancellationToken);
}

public record RemoteSessionStart(
  Guid SessionId,
  Guid TenantId,
  Guid? DeviceId,
  string DeviceName,
  Guid UserId,
  string UserName,
  string? IpAddress,
  RemoteSessionKind Kind = RemoteSessionKind.RemoteControl,
  string? Detail = null);

/// <summary>
/// Records who connected to which device. Runs outside the request that started a session,
/// because the session ends when the relay closes rather than in any hub call.
/// </summary>
public class RemoteSessionLogger(
  IDbContextFactory<AppDb> dbContextFactory,
  IHttpContextAccessor httpContextAccessor,
  TimeProvider timeProvider)
  : IRemoteSessionLogger, IRelaySessionObserver
{
  private const int MaxDetailLength = 512;
  private const int MaxFailureReasonLength = 512;
  private const string NeverConnectedReason = "The viewer and the device never connected.";

  private static readonly TimeSpan _endWriteTimeout = TimeSpan.FromSeconds(10);

  private readonly IDbContextFactory<AppDb> _dbContextFactory = dbContextFactory;
  private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
  private readonly TimeProvider _timeProvider = timeProvider;

  public Task LogEnded(Guid sessionId)
  {
    return CloseOpenLogs(sessionId, failureReason: null);
  }

  public Task LogFailed(Guid sessionId, string reason)
  {
    return CloseOpenLogs(sessionId, reason);
  }

  public async Task LogFileOperation(
    Device device,
    ClaimsPrincipal user,
    string detail,
    CancellationToken cancellationToken)
  {
    await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

    var now = _timeProvider.GetUtcNow();
    _ = user.TryGetUserId(out var userId);

    db.RemoteSessionLogs.Add(new RemoteSessionLog
    {
      Detail = Truncate(detail, MaxDetailLength),
      DeviceId = device.Id,
      DeviceName = device.Name,
      EndedAt = now,
      IpAddress = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
      Kind = RemoteSessionKind.FileOperation,
      SessionId = Guid.NewGuid(),
      StartedAt = now,
      TenantId = device.TenantId,
      UserId = userId,
      UserName = user.Identity?.Name ?? "Unknown",
    });

    await db.SaveChangesAsync(cancellationToken);
  }

  public async Task LogStarted(RemoteSessionStart session, CancellationToken cancellationToken)
  {
    await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

    db.RemoteSessionLogs.Add(new RemoteSessionLog
    {
      Detail = session.Detail is null ? null : Truncate(session.Detail, MaxDetailLength),
      DeviceId = session.DeviceId,
      DeviceName = session.DeviceName,
      IpAddress = session.IpAddress,
      Kind = session.Kind,
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

  private static string Truncate(string value, int maxLength)
  {
    return value.Length <= maxLength ? value : value[..maxLength];
  }

  private async Task CloseOpenLogs(Guid sessionId, string? failureReason)
  {
    // The caller's token is often already cancelled by the disconnect being recorded.
    using var cts = new CancellationTokenSource(_endWriteTimeout);

    await using var db = await _dbContextFactory.CreateDbContextAsync(cts.Token);

    // Every relay session passes through here, and some of them have no log.
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
      log.FailureReason = failureReason is null ? null : Truncate(failureReason, MaxFailureReasonLength);
      log.WasInterrupted = false;
    }

    await db.SaveChangesAsync(cts.Token);
  }
}
