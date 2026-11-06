using ControlR.DesktopClient.Common.ServiceInterfaces;
using ControlR.DesktopClient.Common.Services;
using ControlR.Libraries.Api.Contracts.Dtos.RemoteControlDtos;
using ControlR.Libraries.Shared.Primitives;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace ControlR.DesktopClient.Windows.Services;

/// <summary>
/// Captures the default output device through WASAPI loopback, which only ever sees
/// what the device is playing. It has no access to any microphone.
/// </summary>
internal sealed class AudioCapturerWindows(ILogger<AudioCapturerWindows> logger) : IAudioCapturer
{
  private readonly ILogger<AudioCapturerWindows> _logger = logger;

  private WasapiLoopbackCapture? _capture;

  public Result Start(Action<AudioChunkDto> onChunk)
  {
    try
    {
      var capture = new WasapiLoopbackCapture();
      var format = capture.WaveFormat;

      if (format.Encoding != WaveFormatEncoding.IeeeFloat || format.BitsPerSample != 32)
      {
        capture.Dispose();
        return Result.Fail($"Unsupported audio format: {format.Encoding}, {format.BitsPerSample}-bit.");
      }

      capture.DataAvailable += (_, args) =>
      {
        var chunk = AudioPcmConverter.ConvertFloat32(
          args.Buffer.AsSpan(0, args.BytesRecorded),
          format.SampleRate,
          format.Channels);

        if (chunk is not null)
        {
          onChunk(chunk);
        }
      };

      capture.StartRecording();
      _capture = capture;

      _logger.LogInformation(
        "Audio capture started. Sample rate: {SampleRate}, Channels: {Channels}.",
        format.SampleRate,
        format.Channels);

      return Result.Ok();
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to start audio capture.");
      return Result.Fail("This device has no audio output available to capture.");
    }
  }

  public void Stop()
  {
    var capture = _capture;
    _capture = null;
    if (capture is null)
    {
      return;
    }

    try
    {
      capture.StopRecording();
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error while stopping audio capture.");
    }
    finally
    {
      capture.Dispose();
    }
  }
}
