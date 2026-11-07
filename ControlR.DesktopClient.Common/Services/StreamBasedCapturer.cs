using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Bitbound.SimpleMessenger;
using ControlR.DesktopClient.Common.Messages;
using ControlR.DesktopClient.Common.Models;
using ControlR.DesktopClient.Common.ServiceInterfaces;
using ControlR.DesktopClient.Common.Services.Encoders;
using ControlR.DesktopClient.Common.State;
using ControlR.Libraries.Api.Contracts.Dtos.RemoteControlDtos;
using ControlR.Libraries.Api.Contracts.Enums;
using ControlR.Libraries.Shared.Primitives;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace ControlR.DesktopClient.Common.Services;

/// <summary>
/// Sends the screen as a video stream. Unlike image frames, a lost or late frame cannot be
/// redrawn on its own, so anything that disturbs the stream restarts it from a key frame.
/// </summary>
internal sealed class StreamBasedCapturer : IDesktopCapturer
{
  private const int MaxEncoderFailures = 3;

  private static readonly TimeSpan _failureDelay = TimeSpan.FromMilliseconds(100);
  private static readonly TimeSpan _frameInterval = TimeSpan.FromSeconds(1.0 / 30);
  private static readonly TimeSpan _minKeyFrameInterval = TimeSpan.FromMilliseconds(500);
  private static readonly TimeSpan _noChangeDelay = TimeSpan.FromMilliseconds(10);

  private readonly Channel<RemoteControlDtoWrapper> _channel = Channel.CreateBounded<RemoteControlDtoWrapper>(
    new BoundedChannelOptions(capacity: 2)
    {
      FullMode = BoundedChannelFullMode.Wait,
      SingleReader = true,
      SingleWriter = true,
    });
  private readonly IDisplayManager _displayManager;
  private readonly IStreamEncoder _encoder;
  private readonly ConcurrentQueue<DateTimeOffset> _framesSent = [];
  private readonly ILogger<StreamBasedCapturer> _logger;
  private readonly IMessenger _messenger;
  private readonly IDisposable _messengerRegistration;
  private readonly IScreenGrabber _screenGrabber;
  private readonly IRemoteControlSessionState _sessionState;
  private readonly CancellationTokenSource _stopCts = new();
  private readonly TimeProvider _timeProvider;

  private StreamEncoderSettings? _activeSettings;
  private Task? _captureTask;
  private bool _disposed;
  private int _encoderFailures;
  private volatile bool _forceKeyFrame = true;
  private DateTimeOffset _lastEncoderStart;
  private byte[] _lastPixels = [];
  private volatile DisplayInfo? _selectedDisplay;
  private Task? _sendTask;

  public StreamBasedCapturer(
    IScreenGrabber screenGrabber,
    IStreamEncoder encoder,
    IDisplayManager displayManager,
    IMessenger messenger,
    IRemoteControlSessionState sessionState,
    TimeProvider timeProvider,
    ILogger<StreamBasedCapturer> logger)
  {
    _screenGrabber = screenGrabber;
    _encoder = encoder;
    _displayManager = displayManager;
    _messenger = messenger;
    _sessionState = sessionState;
    _timeProvider = timeProvider;
    _logger = logger;

    _messengerRegistration = messenger.Register<DisplaySettingsChangedMessage>(this, HandleDisplaySettingsChanged);
  }

  public async Task ChangeDisplays(string displayId)
  {
    var findResult = await _displayManager.TryFindDisplay(displayId);
    if (!findResult.IsSuccess)
    {
      _logger.LogWarning("Could not find display with ID {DisplayId} when changing displays.", displayId);
      return;
    }

    _selectedDisplay = findResult.Value;
    _forceKeyFrame = true;
  }

  public async ValueTask DisposeAsync()
  {
    if (_disposed)
    {
      return;
    }

    _disposed = true;
    _messengerRegistration.Dispose();
    await _stopCts.CancelAsync();

    foreach (var task in new[] { _captureTask, _sendTask })
    {
      if (task is null)
      {
        continue;
      }

      try
      {
        await task;
      }
      catch (OperationCanceledException)
      {
        // Stopping is what was asked for.
      }
    }

    _encoder.Dispose();
    _stopCts.Dispose();
  }

  public string GetCaptureMode() => "VP9";

  public async IAsyncEnumerable<RemoteControlDtoWrapper> GetCaptureStream(
    [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    await foreach (var item in _channel.Reader.ReadAllAsync(cancellationToken))
    {
      yield return item;
    }
  }

  public double GetCurrentFps(TimeSpan window)
  {
    var cutoff = _timeProvider.GetUtcNow() - window;
    while (_framesSent.TryPeek(out var timestamp) && timestamp < cutoff)
    {
      _ = _framesSent.TryDequeue(out _);
    }

    return _framesSent.Count / window.TotalSeconds;
  }

  public int GetCurrentQuality() => GetQuality();

  public Task RequestKeyFrame()
  {
    _forceKeyFrame = true;
    return Task.CompletedTask;
  }

  public async Task StartCapturingChanges(CancellationToken cancellationToken)
  {
    if (_captureTask is not null)
    {
      return;
    }

    _selectedDisplay ??= await _displayManager.GetPrimaryDisplay();

    var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopCts.Token);
    _sendTask = Task.Run(() => SendFrames(linked.Token), linked.Token);
    _captureTask = Task.Run(() => CaptureFrames(linked.Token), linked.Token);
  }

  public Task<Result<DisplayInfo>> TryGetSelectedDisplay()
  {
    return Task.FromResult(_selectedDisplay is { } selected
      ? Result.Ok(selected)
      : Result.Fail<DisplayInfo>("No display selected."));
  }

  private async Task CaptureFrames(CancellationToken cancellationToken)
  {
    while (!cancellationToken.IsCancellationRequested)
    {
      var started = _timeProvider.GetTimestamp();

      try
      {
        if (!await CaptureOneFrame(cancellationToken))
        {
          return;
        }
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error while capturing a video frame.");
        await Task.Delay(_failureDelay, _timeProvider, cancellationToken);
      }

      var remaining = _frameInterval - _timeProvider.GetElapsedTime(started);
      if (remaining > TimeSpan.Zero)
      {
        await Task.Delay(remaining, _timeProvider, cancellationToken);
      }
    }
  }

  /// <summary>
  /// Returns false once the encoder has given up, which ends the capture.
  /// </summary>
  private async Task<bool> CaptureOneFrame(CancellationToken cancellationToken)
  {
    if (_selectedDisplay is not { } display)
    {
      await Task.Delay(_failureDelay, _timeProvider, cancellationToken);
      return true;
    }

    var needsKeyFrame = _forceKeyFrame || !_encoder.IsRunning;

    using var capture = await _screenGrabber.CaptureDisplay(
      targetDisplay: display,
      captureCursor: _sessionState.CaptureCursor,
      forceKeyFrame: needsKeyFrame);

    if (!capture.IsSuccess)
    {
      var delay = capture.HadNoChanges ? _noChangeDelay : _failureDelay;
      await Task.Delay(delay, _timeProvider, cancellationToken);
      return true;
    }

    var settings = new StreamEncoderSettings(
      capture.Bitmap.Width,
      capture.Bitmap.Height,
      GetQuality(),
      _sessionState.IsMaxBandwidthEnabled ? _sessionState.MaxBandwidthMbps : null,
      capture.Bitmap.ColorType);

    if (ShouldStartEncoder(settings, needsKeyFrame))
    {
      if (!TryStartEncoder(settings))
      {
        return false;
      }
    }
    else if (!HasChanged(capture.Bitmap))
    {
      return true;
    }

    await _encoder.EncodeFrame(capture.Bitmap, cancellationToken);
    return true;
  }

  private int GetQuality()
  {
    return _sessionState.IsAutoQualityEnabled
      ? _sessionState.AutoQualityMaximum
      : _sessionState.ImageQuality;
  }

  private async Task HandleDisplaySettingsChanged(object subscriber, DisplaySettingsChangedMessage message)
  {
    try
    {
      await _displayManager.ReloadDisplays();

      var findResult = _selectedDisplay is { } selected
        ? await _displayManager.TryFindDisplay(selected.DeviceName)
        : null;

      _selectedDisplay = findResult is { IsSuccess: true }
        ? findResult.Value
        : await _displayManager.GetPrimaryDisplay();

      _forceKeyFrame = true;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error while handling display settings changed.");
    }
  }

  /// <summary>
  /// An unchanged screen costs nothing to skip, and the viewer keeps showing the last frame.
  /// </summary>
  private bool HasChanged(SKBitmap bitmap)
  {
    var pixels = bitmap.GetPixelSpan();
    if (pixels.SequenceEqual(_lastPixels))
    {
      return false;
    }

    if (_lastPixels.Length != pixels.Length)
    {
      _lastPixels = new byte[pixels.Length];
    }

    pixels.CopyTo(_lastPixels);
    return true;
  }

  private async Task SendFrames(CancellationToken cancellationToken)
  {
    try
    {
      await foreach (var frame in _encoder.Frames.ReadAllAsync(cancellationToken))
      {
        _encoderFailures = 0;

        var now = _timeProvider.GetUtcNow();
        _framesSent.Enqueue(now);

        var dto = new VideoStreamPacketDto(frame.Data, now.ToUnixTimeMilliseconds(), frame.IsKeyFrame);
        var wrapper = RemoteControlDtoWrapper.Create(dto, RemoteControlDtoType.VideoStreamPacket);
        await _channel.Writer.WriteAsync(wrapper, cancellationToken);
      }
    }
    catch (OperationCanceledException)
    {
      // The session ended.
    }
  }

  private bool ShouldStartEncoder(StreamEncoderSettings settings, bool needsKeyFrame)
  {
    if (!_encoder.IsRunning || settings != _activeSettings)
    {
      return true;
    }

    // A viewer can ask repeatedly while it waits, and each restart would throw away a key frame.
    return needsKeyFrame && _timeProvider.GetUtcNow() - _lastEncoderStart >= _minKeyFrameInterval;
  }

  private bool TryStartEncoder(StreamEncoderSettings settings)
  {
    if (_activeSettings is not null && !_encoder.IsRunning && ++_encoderFailures > MaxEncoderFailures)
    {
      _logger.LogError("ffmpeg keeps stopping, so video capture has ended.");
      _messenger.Send(new SendToastToViewerMessage(
        "The VP9 encoder stopped. Reconnect with VP9 turned off.",
        MessageSeverity.Error));
      return false;
    }

    try
    {
      _encoder.Start(settings);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to start the VP9 encoder.");
      _messenger.Send(new SendToastToViewerMessage(
        "The VP9 encoder could not start. Reconnect with VP9 turned off.",
        MessageSeverity.Error));
      return false;
    }

    _activeSettings = settings;
    _forceKeyFrame = false;
    _lastEncoderStart = _timeProvider.GetUtcNow();
    return true;
  }
}
