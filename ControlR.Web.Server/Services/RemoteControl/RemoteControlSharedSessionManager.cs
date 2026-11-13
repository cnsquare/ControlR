using System.Security.Cryptography;

namespace ControlR.Web.Server.Services.RemoteControl;

/// <summary>
/// The result of starting a shared remote-control (fan-out) session. The desktop connects to the
/// fan-out endpoint as the producer with <see cref="ProducerToken"/>; each invited technician connects
/// as a viewer with <see cref="ViewerToken"/>. Tokens are secrets — never log or expose the producer
/// token to a viewer.
/// </summary>
public sealed record SharedSessionGrant(Guid SessionId, string ProducerToken, string ViewerToken);

/// <summary>
/// Creates and tears down shared remote-control sessions: mints the producer and viewer tokens, and
/// registers / removes them in the <see cref="IRemoteControlSharedSessionStore"/>. The fan-out itself
/// is created when the desktop producer connects and removed when it disconnects; ending a session here
/// also drops any live fan-out so viewers are disconnected.
/// </summary>
public interface IRemoteControlSharedSessionManager
{
  SharedSessionGrant StartSession();
  void EndSession(Guid sessionId);
}

public sealed class RemoteControlSharedSessionManager(
  IRemoteControlSharedSessionStore store,
  IRemoteControlFanoutRegistry registry) : IRemoteControlSharedSessionManager
{
  private readonly IRemoteControlFanoutRegistry _registry = registry;
  private readonly IRemoteControlSharedSessionStore _store = store;

  public SharedSessionGrant StartSession()
  {
    var sessionId = Guid.NewGuid();
    var producerToken = GenerateToken();
    var viewerToken = GenerateToken();
    _store.Register(sessionId, producerToken, viewerToken);
    return new SharedSessionGrant(sessionId, producerToken, viewerToken);
  }

  public void EndSession(Guid sessionId)
  {
    // Drop the live fan-out first so every viewer is disconnected, then revoke the tokens.
    _registry.Remove(sessionId);
    _store.Remove(sessionId);
  }

  private static string GenerateToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
