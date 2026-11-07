using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;
using ControlR.Libraries.Api.Contracts.Enums;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace ControlR.DesktopClient.Common.Services.Encoders;

/// <summary>
/// Streams frames through ffmpeg. ffmpeg cannot be told to emit a key frame on demand through a
/// pipe, so a key frame is made by starting it again, which costs well under a second.
/// </summary>
public sealed class Vp9Encoder(IFfmpegLocator ffmpegLocator, ILogger<Vp9Encoder> logger) : IStreamEncoder
{
  private const int BytesPerPixel = 4;
  private const int MaxCrf = 63;
  private const int MinCrf = 4;

  private static readonly TimeSpan _stopTimeout = TimeSpan.FromSeconds(3);

  // Small, so a slow connection slows capture instead of building a backlog of old frames.
  private readonly Channel<EncodedVideoFrame> _frames = Channel.CreateBounded<EncodedVideoFrame>(
    new BoundedChannelOptions(capacity: 4)
    {
      FullMode = BoundedChannelFullMode.Wait,
      SingleWriter = true,
    });
  private readonly IFfmpegLocator _ffmpegLocator = ffmpegLocator;
  private readonly ILogger<Vp9Encoder> _logger = logger;

  private Run? _run;
  private byte[] _writeBuffer = [];

  public ChannelReader<EncodedVideoFrame> Frames => _frames.Reader;

  public bool IsRunning => _run is { Process.HasExited: false };

  public CaptureEncoderType Type => CaptureEncoderType.Vpx;

  public static IReadOnlyList<string> BuildArguments(StreamEncoderSettings settings)
  {
    var crf = Math.Clamp((int)Math.Round(MaxCrf - settings.Quality * 0.55), MinCrf, MaxCrf);
    var bitrate = settings.MaxMbps is { } maxMbps
      ? $"{(int)(maxMbps * 1000)}k"
      : "0";

    return
    [
      "-hide_banner", "-loglevel", "error",
      "-f", "rawvideo",
      "-pixel_format", settings.ColorType == SKColorType.Rgba8888 ? "rgba" : "bgra",
      "-video_size", string.Create(CultureInfo.InvariantCulture, $"{settings.Width}x{settings.Height}"),
      "-use_wallclock_as_timestamps", "1",
      "-i", "pipe:0",
      "-c:v", "libvpx-vp9",
      "-deadline", "realtime",
      "-cpu-used", "8",
      "-row-mt", "1",
      "-tile-columns", "2",
      "-threads", "4",
      // Without these two the encoder holds frames back to look ahead, which is added latency.
      "-lag-in-frames", "0",
      "-auto-alt-ref", "0",
      "-g", "600",
      "-crf", crf.ToString(CultureInfo.InvariantCulture),
      "-b:v", bitrate,
      "-pix_fmt", "yuv420p",
      "-fps_mode", "passthrough",
      "-flush_packets", "1",
      "-f", "ivf",
      "pipe:1",
    ];
  }

  public void Dispose()
  {
    Stop();
    _frames.Writer.TryComplete();
  }

  public async Task EncodeFrame(SKBitmap frame, CancellationToken cancellationToken)
  {
    if (_run is not { } run)
    {
      return;
    }

    var rowLength = frame.Width * BytesPerPixel;
    var length = rowLength * frame.Height;
    if (_writeBuffer.Length != length)
    {
      _writeBuffer = new byte[length];
    }

    CopyPixels(frame, rowLength);

    try
    {
      await run.Input.WriteAsync(_writeBuffer, cancellationToken);
      await run.Input.FlushAsync(cancellationToken);
    }
    catch (IOException ex)
    {
      // The caller sees IsRunning go false and decides what happens next.
      _logger.LogWarning(ex, "ffmpeg stopped accepting frames.");
    }
  }

  public void Start(StreamEncoderSettings settings)
  {
    Stop();

    var path = _ffmpegLocator.FindVp9Capable()
      ?? throw new InvalidOperationException("No ffmpeg with VP9 support was found.");

    var startInfo = new ProcessStartInfo
    {
      FileName = path,
      CreateNoWindow = true,
      RedirectStandardError = true,
      RedirectStandardInput = true,
      RedirectStandardOutput = true,
      UseShellExecute = false,
    };

    foreach (var argument in BuildArguments(settings))
    {
      startInfo.ArgumentList.Add(argument);
    }

    var process = Process.Start(startInfo)
      ?? throw new InvalidOperationException("Failed to start ffmpeg.");

    process.ErrorDataReceived += (_, e) =>
    {
      if (!string.IsNullOrWhiteSpace(e.Data))
      {
        _logger.LogWarning("ffmpeg: {Message}", e.Data);
      }
    };
    process.BeginErrorReadLine();

    var cts = new CancellationTokenSource();
    var pump = PumpFrames(process.StandardOutput.BaseStream, cts.Token);
    _run = new Run(process, process.StandardInput.BaseStream, pump, cts);
  }

  private void CopyPixels(SKBitmap frame, int rowLength)
  {
    var source = frame.GetPixelSpan();
    if (frame.RowBytes == rowLength)
    {
      source[.._writeBuffer.Length].CopyTo(_writeBuffer);
      return;
    }

    for (var row = 0; row < frame.Height; row++)
    {
      source.Slice(row * frame.RowBytes, rowLength).CopyTo(_writeBuffer.AsSpan(row * rowLength));
    }
  }

  private async Task PumpFrames(Stream output, CancellationToken cancellationToken)
  {
    try
    {
      await foreach (var frame in IvfReader.ReadFrames(output, cancellationToken))
      {
        await _frames.Writer.WriteAsync(
          new EncodedVideoFrame(frame, Vp9Bitstream.IsKeyFrame(frame)),
          cancellationToken);
      }
    }
    catch (OperationCanceledException)
    {
      // Stopped on purpose.
    }
    catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidDataException or ChannelClosedException)
    {
      _logger.LogWarning(ex, "Stopped reading from ffmpeg.");
    }
  }

  private void Stop()
  {
    if (_run is not { } run)
    {
      return;
    }

    _run = null;
    run.Cancellation.Cancel();

    try
    {
      if (!run.Process.HasExited)
      {
        run.Process.Kill(entireProcessTree: true);
      }

      // Waited for so a stale frame from this run cannot follow the next run's key frame.
      run.Pump.Wait(_stopTimeout);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error while stopping ffmpeg.");
    }

    run.Process.Dispose();
    run.Cancellation.Dispose();
  }

  private sealed record Run(Process Process, Stream Input, Task Pump, CancellationTokenSource Cancellation);
}
