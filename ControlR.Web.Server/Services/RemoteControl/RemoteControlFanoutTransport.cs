using System.Buffers.Binary;
using System.Threading.Channels;
using MessagePack;

namespace ControlR.Web.Server.Services.RemoteControl;

/// <summary>
/// Moves encoded video frames between a stream and a <see cref="RemoteControlSessionFanout"/>: the
/// desktop producer's frames are read off a stream into the fan-out, and each viewer's frames are
/// written from the fan-out out to a stream. Frames are length-prefixed MessagePack
/// <see cref="VideoStreamPacketDto"/> so multiple frames can be read back deterministically.
///
/// Transport-agnostic by design — the stream is an in-memory pipe in tests and a WebSocket (wrapped as
/// a stream) in the endpoint. Keeping the pump logic here, off the socket, is what makes it testable.
/// </summary>
public static class RemoteControlFanoutTransport
{
  // A single encoded frame should never approach this; it bounds a malformed/hostile length prefix.
  public const int MaxFrameBytes = 32 * 1024 * 1024;

  /// <summary>
  /// Reads frames from the producer stream and publishes them to the fan-out until the stream ends.
  /// </summary>
  public static async Task PumpProducerAsync(
    Stream source,
    RemoteControlSessionFanout fanout,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(source);
    ArgumentNullException.ThrowIfNull(fanout);

    while (!cancellationToken.IsCancellationRequested)
    {
      var packet = await ReadFrameAsync(source, cancellationToken).ConfigureAwait(false);
      if (packet is null)
      {
        // Producer closed the stream.
        return;
      }

      fanout.Publish(packet);
    }
  }

  /// <summary>
  /// Writes frames from a viewer's fan-out channel to the destination stream until the channel
  /// completes (the viewer left or the session ended).
  /// </summary>
  public static async Task PumpViewerAsync(
    Stream destination,
    ChannelReader<VideoStreamPacketDto> reader,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(destination);
    ArgumentNullException.ThrowIfNull(reader);

    await foreach (var packet in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
    {
      await WriteFrameAsync(destination, packet, cancellationToken).ConfigureAwait(false);
      await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
  }

  /// <summary>
  /// Writes one length-prefixed MessagePack frame to the stream.
  /// </summary>
  public static async Task WriteFrameAsync(Stream stream, VideoStreamPacketDto packet, CancellationToken cancellationToken)
  {
    var payload = MessagePackSerializer.Serialize(packet, cancellationToken: cancellationToken);

    // Write the length prefix and payload as one buffer so a message-based transport (a WebSocket)
    // carries exactly one frame per message.
    var framed = new byte[4 + payload.Length];
    BinaryPrimitives.WriteInt32LittleEndian(framed, payload.Length);
    payload.CopyTo(framed, 4);
    await stream.WriteAsync(framed, cancellationToken).ConfigureAwait(false);
  }

  /// <summary>
  /// Reads one length-prefixed MessagePack frame, or null when the stream ends cleanly before a frame.
  /// </summary>
  public static async Task<VideoStreamPacketDto?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
  {
    var prefix = new byte[4];
    if (!await ReadExactlyOrEofAsync(stream, prefix, cancellationToken).ConfigureAwait(false))
    {
      return null;
    }

    var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
    if (length is <= 0 or > MaxFrameBytes)
    {
      throw new InvalidDataException($"Invalid frame length {length}.");
    }

    var payload = new byte[length];
    if (!await ReadExactlyOrEofAsync(stream, payload, cancellationToken).ConfigureAwait(false))
    {
      throw new EndOfStreamException("Stream ended mid-frame.");
    }

    return MessagePackSerializer.Deserialize<VideoStreamPacketDto>(payload, cancellationToken: cancellationToken);
  }

  // Reads exactly buffer.Length bytes. Returns false if the stream ends before any byte (a clean
  // close on a frame boundary); throws if it ends after a partial read (a truncated frame).
  private static async Task<bool> ReadExactlyOrEofAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
  {
    var offset = 0;
    while (offset < buffer.Length)
    {
      var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
      if (read == 0)
      {
        if (offset == 0)
        {
          return false;
        }

        throw new EndOfStreamException("Stream ended mid-frame.");
      }

      offset += read;
    }

    return true;
  }
}
