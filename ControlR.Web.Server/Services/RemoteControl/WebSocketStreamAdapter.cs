using System.Net.WebSockets;

namespace ControlR.Web.Server.Services.RemoteControl;

/// <summary>
/// Presents a binary <see cref="WebSocket"/> as a continuous read/write <see cref="Stream"/>, so the
/// stream-based fan-out transport can run over a socket. Reads concatenate the bytes of incoming binary
/// messages (frame boundaries are the transport's length prefix, not the WebSocket message boundary);
/// each write is sent as one binary message. A received close frame surfaces as end-of-stream (Read
/// returns 0).
/// </summary>
public sealed class WebSocketStreamAdapter(WebSocket webSocket) : Stream
{
  private readonly WebSocket _webSocket = webSocket;
  private byte[] _pending = [];
  private int _pendingLength;
  private int _pendingOffset;

  public override bool CanRead => true;
  public override bool CanSeek => false;
  public override bool CanWrite => true;
  public override long Length => throw new NotSupportedException();
  public override long Position
  {
    get => throw new NotSupportedException();
    set => throw new NotSupportedException();
  }

  public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
  {
    if (buffer.Length == 0)
    {
      return 0;
    }

    while (_pendingOffset >= _pendingLength)
    {
      if (_pending.Length == 0)
      {
        _pending = new byte[8192];
      }

      var result = await _webSocket.ReceiveAsync(_pending, cancellationToken).ConfigureAwait(false);
      if (result.MessageType == WebSocketMessageType.Close)
      {
        return 0;
      }

      _pendingOffset = 0;
      _pendingLength = result.Count;
    }

    var toCopy = Math.Min(buffer.Length, _pendingLength - _pendingOffset);
    _pending.AsSpan(_pendingOffset, toCopy).CopyTo(buffer.Span);
    _pendingOffset += toCopy;
    return toCopy;
  }

  public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
  {
    await _webSocket.SendAsync(buffer, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken)
      .ConfigureAwait(false);
  }

  public override int Read(byte[] buffer, int offset, int count) =>
    ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

  public override void Write(byte[] buffer, int offset, int count) =>
    WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

  public override void Flush()
  {
  }

  public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

  public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

  public override void SetLength(long value) => throw new NotSupportedException();
}
