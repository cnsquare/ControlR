using System.Collections.Concurrent;
using System.Threading.Channels;

namespace ControlR.Web.Server.Services.RemoteControl;

/// <summary>
/// Fans one desktop's encoded video stream out to many viewers of a single shared remote-control
/// session (Phase 1 of multi-viewer: view-only). A decoder can only start from a key frame, so the
/// fan-out keeps the current GOP — the latest key frame and every delta frame since — and replays it
/// to a late-joining viewer before live frames, so they decode immediately without a round-trip to the
/// desktop. A slow viewer's queue drops its oldest frames rather than stalling the broadcast; it
/// recovers at the next key frame.
///
/// This is the transport-agnostic core: the caller feeds frames in via <see cref="Publish"/> and each
/// viewer reads from the channel returned by <see cref="Subscribe"/>. It does not decide how frames
/// arrive from the desktop or reach the browser — that wiring is a later increment.
/// </summary>
public sealed class RemoteControlSessionFanout : IDisposable
{
  private readonly int _gopBufferLimit;
  private readonly List<VideoStreamPacketDto> _gopBuffer = [];
  private readonly ConcurrentDictionary<Guid, Channel<VideoStreamPacketDto>> _subscribers = new();
  private readonly int _subscriberQueueCapacity;
  private readonly Lock _sync = new();
  private bool _disposed;

  /// <param name="subscriberQueueCapacity">
  /// Max frames buffered per viewer before the oldest is dropped. Must be at least the GOP limit so a
  /// new viewer's replayed GOP always fits.
  /// </param>
  /// <param name="gopBufferLimit">
  /// Max frames retained for late-join replay. If a GOP grows past this (key frames too infrequent),
  /// replay is skipped and the viewer waits for the next key frame.
  /// </param>
  public RemoteControlSessionFanout(int subscriberQueueCapacity = 300, int gopBufferLimit = 120)
  {
    if (subscriberQueueCapacity < gopBufferLimit)
    {
      throw new ArgumentException(
        "Subscriber queue capacity must be at least the GOP buffer limit.",
        nameof(subscriberQueueCapacity));
    }

    _subscriberQueueCapacity = subscriberQueueCapacity;
    _gopBufferLimit = gopBufferLimit;
  }

  public int SubscriberCount => _subscribers.Count;

  /// <summary>
  /// Publishes one frame to every current viewer. Frames must be published in order from a single
  /// producer (the desktop stream).
  /// </summary>
  public void Publish(VideoStreamPacketDto packet)
  {
    ArgumentNullException.ThrowIfNull(packet);

    Channel<VideoStreamPacketDto>[] targets;
    lock (_sync)
    {
      if (_disposed)
      {
        return;
      }

      if (packet.IsKeyFrame)
      {
        // A key frame starts a new GOP; earlier frames are no longer needed for replay.
        _gopBuffer.Clear();
        _gopBuffer.Add(packet);
      }
      else if (_gopBuffer.Count > 0 && _gopBuffer.Count < _gopBufferLimit)
      {
        // Keep delta frames that follow the key frame so a joiner can replay the whole GOP.
        _gopBuffer.Add(packet);
      }
      else if (_gopBuffer.Count >= _gopBufferLimit)
      {
        // GOP outgrew the replay budget: stop buffering so memory is bounded. Joiners wait for the
        // next key frame. Clearing marks the buffer as not-replayable until that key frame arrives.
        _gopBuffer.Clear();
      }

      targets = [.. _subscribers.Values];
    }

    foreach (var channel in targets)
    {
      WriteDroppingOldest(channel, packet);
    }
  }

  /// <summary>
  /// Adds a viewer. The returned reader first yields the current GOP (so the viewer can decode right
  /// away) and then live frames. Call <see cref="Unsubscribe"/> when the viewer leaves.
  /// </summary>
  public ChannelReader<VideoStreamPacketDto> Subscribe(Guid viewerId)
  {
    var channel = Channel.CreateBounded<VideoStreamPacketDto>(new BoundedChannelOptions(_subscriberQueueCapacity)
    {
      FullMode = BoundedChannelFullMode.DropOldest,
      SingleReader = true,
      SingleWriter = false,
    });

    lock (_sync)
    {
      ObjectDisposedException.ThrowIf(_disposed, this);

      // Replay the current GOP so the new viewer starts from a key frame it can decode.
      foreach (var frame in _gopBuffer)
      {
        WriteDroppingOldest(channel, frame);
      }

      // Replace any prior subscription for this viewer id, completing the old channel.
      if (_subscribers.TryGetValue(viewerId, out var existing))
      {
        existing.Writer.TryComplete();
      }

      _subscribers[viewerId] = channel;
    }

    return channel.Reader;
  }

  /// <summary>
  /// Removes a viewer and completes its channel. Other viewers are unaffected.
  /// </summary>
  public void Unsubscribe(Guid viewerId)
  {
    lock (_sync)
    {
      if (_subscribers.TryRemove(viewerId, out var channel))
      {
        channel.Writer.TryComplete();
      }
    }
  }

  public void Dispose()
  {
    lock (_sync)
    {
      if (_disposed)
      {
        return;
      }

      _disposed = true;
      foreach (var channel in _subscribers.Values)
      {
        channel.Writer.TryComplete();
      }

      _subscribers.Clear();
      _gopBuffer.Clear();
    }
  }

  private static void WriteDroppingOldest(Channel<VideoStreamPacketDto> channel, VideoStreamPacketDto packet)
  {
    // DropOldest means TryWrite only fails once the channel is completed, so a slow viewer loses old
    // frames instead of blocking the broadcast.
    channel.Writer.TryWrite(packet);
  }
}
