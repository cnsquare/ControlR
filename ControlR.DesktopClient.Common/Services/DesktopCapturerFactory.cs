using ControlR.DesktopClient.Common.Options;
using ControlR.DesktopClient.Common.ServiceInterfaces;
using ControlR.DesktopClient.Common.Services.Encoders;
using ControlR.Libraries.Api.Contracts.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ControlR.DesktopClient.Common.Services;

public interface IDesktopCapturerFactory
{
  /// <summary>
  ///   Set when the viewer asked for an encoder this device cannot provide, for telling the viewer why.
  /// </summary>
  string? FallbackNotice { get; }

  /// <summary>
  ///   Creates a new instance of IDesktopCapturer, discarding any existing instance.
  /// </summary>
  /// <returns>
  ///   The new IDesktopCapturer instance.
  /// </returns>
  /// 
  IDesktopCapturer CreateNew();
  /// <summary>
  ///   Gets the existing IDesktopCapturer instance, or creates one if it doesn't exist.
  /// </summary>
  /// <returns>
  ///   The existing or newly created IDesktopCapturer instance.
  /// </returns>
  IDesktopCapturer GetOrCreate();
}

public class DesktopCapturerFactory(
  IServiceProvider serviceProvider,
  IFfmpegLocator ffmpegLocator,
  IOptions<RemoteControlSessionOptions> options,
  ILogger<DesktopCapturerFactory> logger) : IDesktopCapturerFactory
{
  private readonly Lock _createLock = new();
  private readonly IFfmpegLocator _ffmpegLocator = ffmpegLocator;
  private readonly ILogger<DesktopCapturerFactory> _logger = logger;
  private readonly IOptions<RemoteControlSessionOptions> _options = options;
  private readonly IServiceProvider _serviceProvider = serviceProvider;

  private volatile IDesktopCapturer? _capturer;

  public string? FallbackNotice { get; private set; }

  public IDesktopCapturer CreateNew()
  {
    using var lockScope = _createLock.EnterScope();
    return CreateCapturer();
  }

  public IDesktopCapturer GetOrCreate()
  {
    using var lockScope = _createLock.EnterScope();

    if (_capturer is not null)
    {
      return _capturer;
    }

    return CreateCapturer();
  }

  private IDesktopCapturer CreateCapturer()
  {
    _capturer = _options.Value.EncoderType switch
    {
      CaptureEncoderType.Image => ActivatorUtilities.CreateInstance<FrameBasedCapturer>(_serviceProvider),
      CaptureEncoderType.Vpx => CreateVideoCapturer(),
      _ => throw new NotSupportedException($"Encoder type {_options.Value.EncoderType} is not supported.")
    };
    return _capturer;
  }

  private IDesktopCapturer CreateVideoCapturer()
  {
    if (_ffmpegLocator.FindVp9Capable() is not null)
    {
      return ActivatorUtilities.CreateInstance<StreamBasedCapturer>(_serviceProvider);
    }

    _logger.LogWarning("VP9 was requested, but no ffmpeg with VP9 support was found. Using image frames.");
    FallbackNotice = "This device has no ffmpeg with VP9 support, so image frames are being used.";
    return ActivatorUtilities.CreateInstance<FrameBasedCapturer>(_serviceProvider);
  }
}
