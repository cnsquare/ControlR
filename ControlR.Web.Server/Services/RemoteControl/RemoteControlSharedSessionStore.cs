using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace ControlR.Web.Server.Services.RemoteControl;

/// <summary>
/// Access control for shared remote-control (fan-out) sessions. A session is registered with two
/// secrets: a producer token the desktop uses to publish, and a viewer token shared with the
/// technicians who may watch. The fan-out endpoint validates the presented token against the role so a
/// viewer can never connect as the producer and inject frames.
/// </summary>
public interface IRemoteControlSharedSessionStore
{
  void Register(Guid sessionId, string producerToken, string viewerToken);
  void Remove(Guid sessionId);
  bool ValidateProducer(Guid sessionId, string token);
  bool ValidateViewer(Guid sessionId, string token);
}

public sealed class RemoteControlSharedSessionStore : IRemoteControlSharedSessionStore
{
  private readonly ConcurrentDictionary<Guid, Tokens> _sessions = new();

  public void Register(Guid sessionId, string producerToken, string viewerToken)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(producerToken);
    ArgumentException.ThrowIfNullOrWhiteSpace(viewerToken);
    _sessions[sessionId] = new Tokens(Hash(producerToken), Hash(viewerToken));
  }

  public void Remove(Guid sessionId) => _sessions.TryRemove(sessionId, out _);

  public bool ValidateProducer(Guid sessionId, string token) =>
    _sessions.TryGetValue(sessionId, out var tokens) && FixedTimeEquals(tokens.ProducerHash, token);

  public bool ValidateViewer(Guid sessionId, string token) =>
    _sessions.TryGetValue(sessionId, out var tokens) && FixedTimeEquals(tokens.ViewerHash, token);

  private static bool FixedTimeEquals(byte[] expectedHash, string presented) =>
    !string.IsNullOrEmpty(presented) &&
    CryptographicOperations.FixedTimeEquals(expectedHash, Hash(presented));

  private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

  private sealed record Tokens(byte[] ProducerHash, byte[] ViewerHash);
}
