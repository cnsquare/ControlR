using System.Net.WebSockets;
using System.Threading.Channels;
using ControlR.Libraries.Api.Contracts.Dtos.RemoteControlDtos;
using ControlR.Libraries.Api.Contracts.RemoteControl;
using Microsoft.Extensions.Logging;

namespace ControlR.DesktopClient.Common.Services;

/// <summary>
/// Publishes the desktop's already-encoded video frames to the server's shared remote-control
/// (fan-out) endpoint as the single producer, so one capture feeds many technician viewers. It is idle
/// until <see cref="StartAsync"/> is called; while idle, <see cref="Publish"/> is a cheap no-op, so the
/// normal one-to-one remote-control path is unaffected.
///
/// Frames are enqueued on a small bounded channel that drops the oldest frame when full — a slow or
/// stalled fan-out link must never back up the capture pipeline. Video recovers from the next key frame.
/// </summary>
public interface ISharedSessionProducer : IAsyncDisposable
{
  bool IsActive { get; }

  /// <summary>
  /// Enqueues one encoded frame for the fan-out. A no-op when no shared session is active, and never
  /// blocks the caller — a full queue drops the oldest frame.
  /// </summary>
  void Publish(VideoStreamPacketDto packet);

  /// <summary>
  /// Connects to the shared fan-out endpoint as the producer and begins forwarding published frames.
  /// Replaces any session already running.
  /// </summary>
  Task StartAsync(Uri serverBaseUri, Guid sessionId, string producerToken, CancellationToken cancellationToken);

  /// <summary>
  /// Ends the shared session and disconnects. Published frames become no-ops again.
  /// </summary>
  Task StopAsync();
}

public sealed class SharedSessionProducer(ILogger<SharedSessionProducer> logger) : ISharedSessionProducer
{
  private readonly ILogger<SharedSessionProducer> _logger = logger;
  private readonly SemaphoreSlim _sync = new(1, 1);

  private Channel<VideoStreamPacketDto>? _channel;
  private ClientWebSocket? _socket;
  private CancellationTokenSource? _cts;
  private Task? _pumpTask;
  private volatile bool _active;

  public bool IsActive => _active;

  public void Publish(VideoStreamPacketDto packet)
  {
    if (!_active)
    {
      return;
    }

    var writer = _channel?.Writer;
    // DropOldest channel: TryWrite never blocks and never fails on a full queue.
    _ = writer?.TryWrite(packet);
  }

  public async Task StartAsync(Uri serverBaseUri, Guid sessionId, string producerToken, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(serverBaseUri);
    await StopAsync().ConfigureAwait(false);

    await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var channel = Channel.CreateBounded<VideoStreamPacketDto>(new BoundedChannelOptions(capacity: 4)
      {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false,
      });

      var socket = new ClientWebSocket();
      var uri = BuildProducerUri(serverBaseUri, sessionId, producerToken);
      await socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);

      var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      _channel = channel;
      _socket = socket;
      _cts = cts;
      _active = true;
      _pumpTask = Task.Run(() => PumpAsync(socket, channel.Reader, cts.Token), CancellationToken.None);

      _logger.LogInformation("Shared remote-control producer connected for session {SessionId}.", sessionId);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to start the shared remote-control producer for session {SessionId}.", sessionId);
      _active = false;
      throw;
    }
    finally
    {
      _sync.Release();
    }
  }

  public async Task StopAsync()
  {
    await _sync.WaitAsync().ConfigureAwait(false);
    try
    {
      if (!_active && _pumpTask is null)
      {
        return;
      }

      _active = false;
      _channel?.Writer.TryComplete();

      if (_cts is not null)
      {
        await _cts.CancelAsync().ConfigureAwait(false);
      }

      if (_pumpTask is not null)
      {
        try
        {
          await _pumpTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
          // Stopping is what was asked for.
        }
        catch (Exception ex)
        {
          _logger.LogDebug(ex, "Shared remote-control producer pump ended with an error.");
        }
      }

      await CloseSocketAsync(_socket).ConfigureAwait(false);

      _socket?.Dispose();
      _cts?.Dispose();
      _socket = null;
      _cts = null;
      _pumpTask = null;
      _channel = null;
    }
    finally
    {
      _sync.Release();
    }
  }

  public async ValueTask DisposeAsync()
  {
    await StopAsync().ConfigureAwait(false);
    _sync.Dispose();
  }

  private async Task PumpAsync(WebSocket socket, ChannelReader<VideoStreamPacketDto> reader, CancellationToken cancellationToken)
  {
    using var adapter = new WebSocketStreamAdapter(socket);
    try
    {
      await foreach (var packet in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
      {
        await RemoteControlFrameCodec.WriteFrameAsync(adapter, packet, cancellationToken).ConfigureAwait(false);
      }
    }
    catch (OperationCanceledException)
    {
      // Session ended.
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Shared remote-control producer link closed.");
      _active = false;
    }
  }

  private static Uri BuildProducerUri(Uri serverBaseUri, Guid sessionId, string producerToken)
  {
    var scheme = serverBaseUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
    var builder = new UriBuilder(serverBaseUri)
    {
      Scheme = scheme,
      Path = RemoteControlFanoutPaths.Path,
      Query = $"sessionId={sessionId}&role=producer&accessToken={Uri.EscapeDataString(producerToken)}",
    };
    return builder.Uri;
  }

  private static async Task CloseSocketAsync(ClientWebSocket? socket)
  {
    if (socket is null || socket.State != WebSocketState.Open)
    {
      return;
    }

    try
    {
      using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
      await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", closeCts.Token).ConfigureAwait(false);
    }
    catch
    {
      // Best-effort close.
    }
  }
}
