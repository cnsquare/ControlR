using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using ControlR.Libraries.Api.Contracts.RemoteControl;

namespace ControlR.Web.Client.Services;

/// <summary>
/// Connects a browser technician to a shared remote-control (fan-out) session as a viewer and yields
/// the producer's already-encoded video frames. It is the symmetric counterpart to the desktop
/// SharedSessionProducer: both frame the stream with <see cref="RemoteControlFrameCodec"/>, so an
/// invited technician decodes the exact same <see cref="VideoStreamPacketDto"/>s the device produced,
/// through the existing WebCodecs draw path. The server replays the latest key frame on join, so a
/// late viewer gets a decodable picture without waiting for the next one.
/// </summary>
public interface ISharedSessionViewerClient : IAsyncDisposable
{
  /// <summary>
  /// Connects as a viewer and streams decoded-ready frames until the session ends or the token is
  /// cancelled. <paramref name="serverBaseUri"/> is the site origin; the fan-out path and scheme are
  /// derived from it.
  /// </summary>
  IAsyncEnumerable<VideoStreamPacketDto> StreamFramesAsync(
    Uri serverBaseUri,
    Guid sessionId,
    string viewerToken,
    CancellationToken cancellationToken);
}

public sealed class SharedSessionViewerClient(ILogger<SharedSessionViewerClient> logger)
  : ISharedSessionViewerClient
{
  private readonly ILogger<SharedSessionViewerClient> _logger = logger;
  private ClientWebSocket? _socket;

  public async IAsyncEnumerable<VideoStreamPacketDto> StreamFramesAsync(
    Uri serverBaseUri,
    Guid sessionId,
    string viewerToken,
    [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(serverBaseUri);

    var socket = new ClientWebSocket();
    _socket = socket;
    var uri = BuildViewerUri(serverBaseUri, sessionId, viewerToken);
    await socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
    _logger.LogInformation("Joined shared remote-control session {SessionId} as a viewer.", sessionId);

    using var adapter = new WebSocketStreamAdapter(socket);

    while (!cancellationToken.IsCancellationRequested)
    {
      VideoStreamPacketDto? packet;
      try
      {
        packet = await RemoteControlFrameCodec.ReadFrameAsync(adapter, cancellationToken).ConfigureAwait(false);
      }
      catch (OperationCanceledException)
      {
        yield break;
      }

      if (packet is null)
      {
        // The producer (or server) closed the stream.
        yield break;
      }

      yield return packet;
    }
  }

  public async ValueTask DisposeAsync()
  {
    var socket = _socket;
    _socket = null;
    if (socket is null)
    {
      return;
    }

    try
    {
      if (socket.State == WebSocketState.Open)
      {
        using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", closeCts.Token).ConfigureAwait(false);
      }
    }
    catch
    {
      // Best-effort close.
    }
    finally
    {
      socket.Dispose();
    }
  }

  private static Uri BuildViewerUri(Uri serverBaseUri, Guid sessionId, string viewerToken)
  {
    var secure = serverBaseUri.Scheme is "https" or "wss";
    var scheme = secure ? "wss" : "ws";
    var builder = new UriBuilder(serverBaseUri)
    {
      Scheme = scheme,
      Path = RemoteControlFanoutPaths.Path,
      Query = $"sessionId={sessionId}&role=viewer&accessToken={Uri.EscapeDataString(viewerToken)}",
    };
    return builder.Uri;
  }
}
