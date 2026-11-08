namespace ControlR.Web.Server.Services.RemoteControl;

/// <summary>
/// Drives one connected party of a shared remote-control session over an already-established transport
/// stream, tying it to the <see cref="IRemoteControlFanoutRegistry"/>. The producer (desktop) owns the
/// session: its fan-out is created when it connects and removed when it leaves (ending the session for
/// every viewer). A viewer subscribes to the live session and is unsubscribed when it leaves, without
/// affecting the others.
///
/// This is the transport-neutral glue between the WebSocket endpoint and the fan-out; the endpoint only
/// accepts the socket, authenticates, wraps it as a stream and calls one of these methods.
/// </summary>
public sealed class RemoteControlFanoutConnectionHandler(
  IRemoteControlFanoutRegistry registry,
  ILogger<RemoteControlFanoutConnectionHandler> logger)
{
  private readonly ILogger<RemoteControlFanoutConnectionHandler> _logger = logger;
  private readonly IRemoteControlFanoutRegistry _registry = registry;

  /// <summary>
  /// Pumps the desktop producer's frames into the session's fan-out until the stream ends, then ends
  /// the session for every viewer.
  /// </summary>
  public async Task HandleProducerAsync(Stream transport, Guid sessionId, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(transport);

    var fanout = _registry.GetOrCreate(sessionId);
    _logger.LogInformation("Shared remote-control producer connected for session {SessionId}.", sessionId);
    try
    {
      await RemoteControlFanoutTransport.PumpProducerAsync(transport, fanout, cancellationToken).ConfigureAwait(false);
    }
    finally
    {
      // The producer is the session's source; once it is gone the session is over for everyone.
      _registry.Remove(sessionId);
      _logger.LogInformation("Shared remote-control producer disconnected; ended session {SessionId}.", sessionId);
    }
  }

  /// <summary>
  /// Subscribes a viewer to the live session and pumps its frames out over the transport until it
  /// leaves or the session ends. Returns false (without touching the transport) when no live session
  /// exists for the id.
  /// </summary>
  public async Task<bool> HandleViewerAsync(
    Stream transport,
    Guid sessionId,
    Guid viewerId,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(transport);

    if (!_registry.TryGet(sessionId, out var fanout))
    {
      _logger.LogWarning("Viewer {ViewerId} requested unknown shared session {SessionId}.", viewerId, sessionId);
      return false;
    }

    var reader = fanout.Subscribe(viewerId);
    _logger.LogInformation("Viewer {ViewerId} joined shared session {SessionId}.", viewerId, sessionId);
    try
    {
      await RemoteControlFanoutTransport.PumpViewerAsync(transport, reader, cancellationToken).ConfigureAwait(false);
    }
    finally
    {
      fanout.Unsubscribe(viewerId);
      _logger.LogInformation("Viewer {ViewerId} left shared session {SessionId}.", viewerId, sessionId);
    }

    return true;
  }
}
