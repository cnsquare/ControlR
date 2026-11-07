using System.Threading.Channels;
using ControlR.Libraries.Api.Contracts.Enums;
using SkiaSharp;

namespace ControlR.DesktopClient.Common.Services.Encoders;

public record EncodedVideoFrame(byte[] Data, bool IsKeyFrame);

// A null MaxMbps leaves the bitrate to the quality setting.
public record StreamEncoderSettings(
  int Width,
  int Height,
  int Quality,
  double? MaxMbps,
  SKColorType ColorType);

public interface IStreamEncoder : IDisposable
{
  ChannelReader<EncodedVideoFrame> Frames { get; }

  bool IsRunning { get; }

  CaptureEncoderType Type { get; }

  Task EncodeFrame(SKBitmap frame, CancellationToken cancellationToken);

  /// <summary>
  /// Starts the encoder, replacing any run in progress. The first frame after this is a key frame.
  /// </summary>
  void Start(StreamEncoderSettings settings);
}
