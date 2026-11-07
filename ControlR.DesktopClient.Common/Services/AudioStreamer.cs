using System.Threading.Channels;
using ControlR.Libraries.Messenger.Extensions;
using ControlR.DesktopClient.Common.Messages;
using ControlR.DesktopClient.Common.ServiceInterfaces;
using ControlR.Libraries.Api.Contracts.Dtos.RemoteControlDtos;
using ControlR.Libraries.Shared.Primitives;
using Microsoft.Extensions.Logging;

namespace ControlR.DesktopClient.Common.Services;

public interface IAudioStreamer
{
  bool IsEnabled { get; }
  Result SetEnabled(bool isEnabled);
}

/// <summary>
/// Moves captured audio to the viewer. The queue is short and drops the oldest audio,
/// so a slow connection loses sound rather than falling further and further behind.
/// </summary>
internal sealed class AudioStreamer(
  IAudioCapturer audioCapturer,
  IMessenger messenger,
  ILogger<AudioStreamer> logger) : IAudioStreamer, IAsyncDisposable
{
  private const int MaxQueuedChunks = 10;

  private readonly IAudioCapturer _audioCapturer = audioCapturer;
  private readonly Lock _lock = new();
  private readonly ILogger<AudioStreamer> _logger = logger;
  private readonly IMessenger _messenger = messenger;

  private Channel<AudioChunkDto>? _chunks;
  private Task? _pump;

  public bool IsEnabled { get; private set; }

  public async ValueTask DisposeAsync()
  {
    SetEnabled(false);

    if (_pump is { } pump)
    {
      await pump.ConfigureAwait(false);
    }
  }

  public Result SetEnabled(bool isEnabled)
  {
    lock (_lock)
    {
      if (isEnabled == IsEnabled)
      {
        return Result.Ok();
      }

      return isEnabled ? Start() : Stop();
    }
  }

  private async Task PumpChunks(ChannelReader<AudioChunkDto> reader)
  {
    try
    {
      await foreach (var chunk in reader.ReadAllAsync().ConfigureAwait(false))
      {
        var wrapper = RemoteControlDtoWrapper.Create(chunk, RemoteControlDtoType.AudioChunk);
        await _messenger.Send(new SendRemoteControlDtoToViewerMessage(wrapper)).ConfigureAwait(false);
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error while sending audio to the viewer.");
    }
  }

  private Result Start()
  {
    var chunks = Channel.CreateBounded<AudioChunkDto>(new BoundedChannelOptions(MaxQueuedChunks)
    {
      FullMode = BoundedChannelFullMode.DropOldest,
      SingleReader = true,
    });

    var result = _audioCapturer.Start(chunk => chunks.Writer.TryWrite(chunk));
    if (!result.IsSuccess)
    {
      return result;
    }

    _chunks = chunks;
    _pump = Task.Run(() => PumpChunks(chunks.Reader));
    IsEnabled = true;
    return Result.Ok();
  }

  private Result Stop()
  {
    _audioCapturer.Stop();
    _chunks?.Writer.TryComplete();
    _chunks = null;
    IsEnabled = false;
    return Result.Ok();
  }
}
