using System.Collections.Concurrent;

namespace ControlR.Web.Server.Services.RemoteControl;

/// <summary>
/// Tracks the live <see cref="RemoteControlSessionFanout"/> for each shared remote-control session, so
/// the desktop producer and every viewer of one session reach the same fan-out. The producer owns the
/// lifecycle: it calls <see cref="GetOrCreate"/> when it starts streaming a shared session and
/// <see cref="Remove"/> when the session ends; viewers reach the fan-out through <see cref="TryGet"/>.
/// </summary>
public interface IRemoteControlFanoutRegistry
{
  int ActiveSessionCount { get; }

  /// <summary>
  /// Returns the fan-out for a shared session, creating it on first use. Idempotent per session id.
  /// </summary>
  RemoteControlSessionFanout GetOrCreate(Guid sessionId);

  /// <summary>
  /// Removes and disposes the fan-out for a session (ending it for every viewer). Safe to call when the
  /// session is unknown.
  /// </summary>
  void Remove(Guid sessionId);

  /// <summary>
  /// Gets the fan-out for a session if one is live.
  /// </summary>
  bool TryGet(Guid sessionId, out RemoteControlSessionFanout fanout);
}

public sealed class RemoteControlFanoutRegistry : IRemoteControlFanoutRegistry, IDisposable
{
  private readonly ConcurrentDictionary<Guid, RemoteControlSessionFanout> _sessions = new();
  private bool _disposed;

  public int ActiveSessionCount => _sessions.Count;

  public RemoteControlSessionFanout GetOrCreate(Guid sessionId)
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
    return _sessions.GetOrAdd(sessionId, _ => new RemoteControlSessionFanout());
  }

  public void Remove(Guid sessionId)
  {
    if (_sessions.TryRemove(sessionId, out var fanout))
    {
      fanout.Dispose();
    }
  }

  public bool TryGet(Guid sessionId, out RemoteControlSessionFanout fanout)
  {
    return _sessions.TryGetValue(sessionId, out fanout!);
  }

  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }

    _disposed = true;
    foreach (var sessionId in _sessions.Keys)
    {
      Remove(sessionId);
    }
  }
}
