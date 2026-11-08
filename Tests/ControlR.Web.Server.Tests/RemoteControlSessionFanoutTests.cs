using System.Threading.Channels;
using ControlR.Libraries.Api.Contracts.Dtos.RemoteControlDtos;
using ControlR.Web.Server.Services.RemoteControl;

namespace ControlR.Web.Server.Tests;

public class RemoteControlSessionFanoutTests
{
  [Fact]
  public void Subscriber_ReceivesFramesPublishedAfterSubscribing()
  {
    using var fanout = new RemoteControlSessionFanout();
    var reader = fanout.Subscribe(Guid.NewGuid());

    fanout.Publish(Key(1));
    fanout.Publish(Delta(2));

    Assert.Equal([1, 2], Timestamps(Drain(reader)));
  }

  [Fact]
  public void LateJoiner_ReplaysCurrentGop_KeyFrameFirst_ThenLiveFrames()
  {
    using var fanout = new RemoteControlSessionFanout();

    fanout.Publish(Key(1));
    fanout.Publish(Delta(2));
    fanout.Publish(Delta(3));

    // Joins mid-GOP: must get the key frame and the deltas since it, in order.
    var reader = fanout.Subscribe(Guid.NewGuid());
    Assert.Equal([1, 2, 3], Timestamps(Drain(reader)));

    fanout.Publish(Delta(4));
    Assert.Equal([4], Timestamps(Drain(reader)));
  }

  [Fact]
  public void NewKeyFrame_ResetsGop_SoJoinerDoesNotReplayOldGop()
  {
    using var fanout = new RemoteControlSessionFanout();

    fanout.Publish(Key(1));
    fanout.Publish(Delta(2));
    fanout.Publish(Key(3));
    fanout.Publish(Delta(4));

    var reader = fanout.Subscribe(Guid.NewGuid());
    Assert.Equal([3, 4], Timestamps(Drain(reader)));
  }

  [Fact]
  public void JoinerBeforeAnyFrame_GetsNothingUntilFirstFrame()
  {
    using var fanout = new RemoteControlSessionFanout();
    var reader = fanout.Subscribe(Guid.NewGuid());

    Assert.Empty(Drain(reader));

    fanout.Publish(Key(1));
    Assert.Equal([1], Timestamps(Drain(reader)));
  }

  [Fact]
  public void WhenGopExceedsLimit_ReplayIsSkippedUntilNextKeyFrame()
  {
    using var fanout = new RemoteControlSessionFanout(subscriberQueueCapacity: 10, gopBufferLimit: 3);

    fanout.Publish(Key(1));
    fanout.Publish(Delta(2));
    fanout.Publish(Delta(3)); // GOP now at the limit (3)
    fanout.Publish(Delta(4)); // overflow -> buffer cleared, no replay available

    var reader = fanout.Subscribe(Guid.NewGuid());
    Assert.Empty(Drain(reader));

    // The next key frame restores replayable state.
    fanout.Publish(Key(5));
    Assert.Equal([5], Timestamps(Drain(fanout.Subscribe(Guid.NewGuid()))));
  }

  [Fact]
  public void SlowViewer_DropsOldestFrames_WithoutBlockingOrAffectingOthers()
  {
    using var fanout = new RemoteControlSessionFanout(subscriberQueueCapacity: 2, gopBufferLimit: 2);

    var slow = fanout.Subscribe(Guid.NewGuid());   // never drained until the end
    var fastId = Guid.NewGuid();
    var fast = fanout.Subscribe(fastId);

    fanout.Publish(Key(1));
    Assert.Equal([1], Timestamps(Drain(fast))); // fast viewer keeps up
    fanout.Publish(Delta(2));
    Assert.Equal([2], Timestamps(Drain(fast)));
    fanout.Publish(Delta(3));
    Assert.Equal([3], Timestamps(Drain(fast)));

    // The slow viewer's capacity-2 queue kept only the last two frames; the oldest were dropped.
    Assert.Equal([2, 3], Timestamps(Drain(slow)));
  }

  [Fact]
  public void Unsubscribe_CompletesThatViewerOnly()
  {
    using var fanout = new RemoteControlSessionFanout();
    var leavingId = Guid.NewGuid();
    var leaving = fanout.Subscribe(leavingId);
    var staying = fanout.Subscribe(Guid.NewGuid());

    fanout.Unsubscribe(leavingId);

    Assert.True(leaving.Completion.IsCompleted);

    fanout.Publish(Key(1));
    Assert.False(staying.Completion.IsCompleted);
    Assert.Equal([1], Timestamps(Drain(staying)));
  }

  [Fact]
  public void SubscriberCount_TracksSubscribeAndUnsubscribe()
  {
    using var fanout = new RemoteControlSessionFanout();
    Assert.Equal(0, fanout.SubscriberCount);

    var a = Guid.NewGuid();
    var b = Guid.NewGuid();
    fanout.Subscribe(a);
    fanout.Subscribe(b);
    Assert.Equal(2, fanout.SubscriberCount);

    fanout.Unsubscribe(a);
    Assert.Equal(1, fanout.SubscriberCount);
  }

  [Fact]
  public void Dispose_CompletesAllViewerChannels()
  {
    var fanout = new RemoteControlSessionFanout();
    var r1 = fanout.Subscribe(Guid.NewGuid());
    var r2 = fanout.Subscribe(Guid.NewGuid());

    fanout.Dispose();

    Assert.True(r1.Completion.IsCompleted);
    Assert.True(r2.Completion.IsCompleted);
  }

  private static VideoStreamPacketDto Key(long ts) => new([(byte)ts], ts, IsKeyFrame: true);
  private static VideoStreamPacketDto Delta(long ts) => new([(byte)ts], ts, IsKeyFrame: false);

  private static List<VideoStreamPacketDto> Drain(ChannelReader<VideoStreamPacketDto> reader)
  {
    var list = new List<VideoStreamPacketDto>();
    while (reader.TryRead(out var packet))
    {
      list.Add(packet);
    }
    return list;
  }

  private static long[] Timestamps(IEnumerable<VideoStreamPacketDto> packets) =>
    [.. packets.Select(x => x.Timestamp)];
}
