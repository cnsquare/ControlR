using System.Threading.Channels;
using ControlR.Libraries.Api.Contracts.RemoteControl;

namespace ControlR.Web.Server.Services.RemoteControl;

/// <summary>
/// Moves encoded video frames between a stream and a <see cref="RemoteControlSessionFanout"/>: the
/// desktop producer's frames are read off a stream into the fan-out, and each viewer's frames are
/// written from the fan-out out to a stream. The wire format (length-prefixed MessagePack
/// <see cref="VideoStreamPacketDto"/>) lives in <see cref="RemoteControlFrameCodec"/> so the desktop
/// producer and browser viewer share exactly the same framing; this type is the server-side pump glue.
///
/// Transport-agnostic by design — the stream is an in-memory pipe in tests and a WebSocket (wrapped as
/// a stream) in the endpoint. Keeping the pump logic here, off the socket, is what makes it testable.
/// </summary>
public static class RemoteControlFanoutTransport
{
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
      var packet = await RemoteControlFrameCodec.ReadFrameAsync(source, cancellationToken).ConfigureAwait(false);
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
      await RemoteControlFrameCodec.WriteFrameAsync(destination, packet, cancellationToken).ConfigureAwait(false);
    }
  }
}
