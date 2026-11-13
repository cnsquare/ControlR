using ControlR.Libraries.Api.Contracts.Dtos.RemoteControlDtos;
using ControlR.Libraries.Api.Contracts.RemoteControl;
using ControlR.Web.Server.Services.RemoteControl;

namespace ControlR.Web.Server.Tests;

public class RemoteControlFanoutTransportTests
{
  private static CancellationToken Ct => TestContext.Current.CancellationToken;

  [Fact]
  public async Task Codec_RoundTripsFrames_AndReturnsNullAtCleanEof()
  {
    using var stream = new MemoryStream();
    await RemoteControlFrameCodec.WriteFrameAsync(stream, Key(1), Ct);
    await RemoteControlFrameCodec.WriteFrameAsync(stream, Delta(2), Ct);
    stream.Position = 0;

    var first = await RemoteControlFrameCodec.ReadFrameAsync(stream, Ct);
    var second = await RemoteControlFrameCodec.ReadFrameAsync(stream, Ct);
    var end = await RemoteControlFrameCodec.ReadFrameAsync(stream, Ct);

    Assert.NotNull(first);
    Assert.Equal(1, first!.Timestamp);
    Assert.True(first.IsKeyFrame);
    Assert.NotNull(second);
    Assert.Equal(2, second!.Timestamp);
    Assert.False(second.IsKeyFrame);
    Assert.Null(end); // clean EOF on a frame boundary
  }

  [Fact]
  public async Task Codec_TruncatedFrame_Throws()
  {
    using var stream = new MemoryStream();
    // A length prefix claiming 10 bytes, but no payload follows.
    stream.Write([10, 0, 0, 0]);
    stream.Position = 0;

    await Assert.ThrowsAsync<EndOfStreamException>(
      async () => await RemoteControlFrameCodec.ReadFrameAsync(stream, Ct));
  }

  [Fact]
  public async Task EndToEnd_LiveViewer_ReceivesPublishedFrames()
  {
    using var fanout = new RemoteControlSessionFanout();
    var viewerId = Guid.NewGuid();
    var reader = fanout.Subscribe(viewerId); // subscribed before production -> live frames

    using var producerSource = new MemoryStream();
    await RemoteControlFrameCodec.WriteFrameAsync(producerSource, Key(1), Ct);
    await RemoteControlFrameCodec.WriteFrameAsync(producerSource, Delta(2), Ct);
    producerSource.Position = 0;

    await RemoteControlFanoutTransport.PumpProducerAsync(producerSource, fanout, Ct);
    fanout.Unsubscribe(viewerId); // completes the channel so the viewer pump finishes

    using var viewerDestination = new MemoryStream();
    await RemoteControlFanoutTransport.PumpViewerAsync(viewerDestination, reader, Ct);

    var received = await ReadAllTimestamps(viewerDestination);
    Assert.Equal(new long[] { 1, 2 }, received);
  }

  [Fact]
  public async Task EndToEnd_LateJoiner_ReplaysCurrentGopThroughTheTransport()
  {
    using var fanout = new RemoteControlSessionFanout();

    using var producerSource = new MemoryStream();
    await RemoteControlFrameCodec.WriteFrameAsync(producerSource, Key(1), Ct);
    await RemoteControlFrameCodec.WriteFrameAsync(producerSource, Delta(2), Ct);
    await RemoteControlFrameCodec.WriteFrameAsync(producerSource, Delta(3), Ct);
    producerSource.Position = 0;

    // Produce the whole GOP first.
    await RemoteControlFanoutTransport.PumpProducerAsync(producerSource, fanout, Ct);

    // Viewer joins afterward: must replay the key frame + deltas since it.
    var viewerId = Guid.NewGuid();
    var reader = fanout.Subscribe(viewerId);
    fanout.Unsubscribe(viewerId);

    using var viewerDestination = new MemoryStream();
    await RemoteControlFanoutTransport.PumpViewerAsync(viewerDestination, reader, Ct);

    var received = await ReadAllTimestamps(viewerDestination);
    Assert.Equal(new long[] { 1, 2, 3 }, received);
  }

  private static VideoStreamPacketDto Key(long ts) => new([(byte)ts], ts, IsKeyFrame: true);
  private static VideoStreamPacketDto Delta(long ts) => new([(byte)ts], ts, IsKeyFrame: false);

  private static async Task<long[]> ReadAllTimestamps(MemoryStream stream)
  {
    stream.Position = 0;
    var timestamps = new List<long>();
    while (await RemoteControlFrameCodec.ReadFrameAsync(stream, Ct) is { } packet)
    {
      timestamps.Add(packet.Timestamp);
    }
    return [.. timestamps];
  }
}
