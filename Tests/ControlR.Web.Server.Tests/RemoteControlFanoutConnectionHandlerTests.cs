using ControlR.Libraries.Api.Contracts.Dtos.RemoteControlDtos;
using ControlR.Libraries.Api.Contracts.RemoteControl;
using ControlR.Web.Server.Services.RemoteControl;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlR.Web.Server.Tests;

public class RemoteControlFanoutConnectionHandlerTests
{
  private static CancellationToken Ct => TestContext.Current.CancellationToken;

  [Fact]
  public async Task HandleViewer_UnknownSession_ReturnsFalse_WithoutTouchingTransport()
  {
    using var registry = new RemoteControlFanoutRegistry();
    var handler = new RemoteControlFanoutConnectionHandler(registry, NullLogger<RemoteControlFanoutConnectionHandler>.Instance);
    using var transport = new MemoryStream();

    var result = await handler.HandleViewerAsync(transport, Guid.NewGuid(), Guid.NewGuid(), Ct);

    Assert.False(result);
    Assert.Equal(0, transport.Length);
  }

  [Fact]
  public async Task HandleProducer_EndsSessionWhenProducerStreamCloses()
  {
    using var registry = new RemoteControlFanoutRegistry();
    var handler = new RemoteControlFanoutConnectionHandler(registry, NullLogger<RemoteControlFanoutConnectionHandler>.Instance);
    var sessionId = Guid.NewGuid();

    using var producerSource = new MemoryStream();
    await RemoteControlFrameCodec.WriteFrameAsync(producerSource, Key(1), Ct);
    producerSource.Position = 0;

    await handler.HandleProducerAsync(producerSource, sessionId, Ct);

    // Producer stream closed -> session removed for everyone.
    Assert.False(registry.TryGet(sessionId, out _));
    Assert.Equal(0, registry.ActiveSessionCount);
  }

  [Fact]
  public async Task HandleViewer_ReceivesSessionFrames_AndUnsubscribesWhenSessionEnds()
  {
    using var registry = new RemoteControlFanoutRegistry();
    var handler = new RemoteControlFanoutConnectionHandler(registry, NullLogger<RemoteControlFanoutConnectionHandler>.Instance);
    var sessionId = Guid.NewGuid();
    var viewerId = Guid.NewGuid();

    // Producer created the session and streamed a key frame (buffered in the GOP for a late joiner).
    var fanout = registry.GetOrCreate(sessionId);
    fanout.Publish(Key(1));

    using var viewerDestination = new MemoryStream();
    var viewerTask = handler.HandleViewerAsync(viewerDestination, sessionId, viewerId, Ct);

    // Wait until the viewer has subscribed, then end the session so the viewer pump completes.
    await WaitForAsync(() => fanout.SubscriberCount == 1);
    registry.Remove(sessionId);

    Assert.True(await viewerTask);
    Assert.Equal(new long[] { 1 }, await ReadAllTimestamps(viewerDestination));
    Assert.Equal(0, fanout.SubscriberCount);
  }

  private static VideoStreamPacketDto Key(long ts) => new([(byte)ts], ts, IsKeyFrame: true);

  private static async Task WaitForAsync(Func<bool> condition)
  {
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
    while (!condition() && DateTime.UtcNow < deadline)
    {
      await Task.Delay(10);
    }

    Assert.True(condition(), "Condition was not met within the timeout.");
  }

  private static async Task<long[]> ReadAllTimestamps(MemoryStream stream)
  {
    stream.Position = 0;
    var timestamps = new List<long>();
    while (await RemoteControlFrameCodec.ReadFrameAsync(stream, TestContext.Current.CancellationToken) is { } packet)
    {
      timestamps.Add(packet.Timestamp);
    }
    return [.. timestamps];
  }
}
