using System.Buffers.Binary;
using ControlR.Libraries.Api.Contracts.Dtos.RemoteControlDtos;

namespace ControlR.Libraries.Api.Contracts.RemoteControl;

/// <summary>
/// The wire format for shared remote-control (fan-out) video frames, shared by the server, the desktop
/// producer and the browser viewer. Each frame is a 4-byte little-endian length prefix followed by the
/// MessagePack-serialized <see cref="VideoStreamPacketDto"/>, so multiple frames can be read back
/// deterministically from a continuous stream (or one frame per WebSocket message).
/// </summary>
public static class RemoteControlFrameCodec
{
  // Bounds a malformed/hostile length prefix; a single encoded frame never approaches this.
  public const int MaxFrameBytes = 32 * 1024 * 1024;

  /// <summary>
  /// Writes one length-prefixed MessagePack frame as a single buffer (so a message-based transport
  /// carries exactly one frame per message).
  /// </summary>
  public static async Task WriteFrameAsync(Stream stream, VideoStreamPacketDto packet, CancellationToken cancellationToken)
  {
    var payload = MessagePackSerializer.Serialize(packet, cancellationToken: cancellationToken);
    var framed = new byte[4 + payload.Length];
    BinaryPrimitives.WriteInt32LittleEndian(framed, payload.Length);
    payload.CopyTo(framed, 4);
    await stream.WriteAsync(framed, cancellationToken).ConfigureAwait(false);
    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
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
