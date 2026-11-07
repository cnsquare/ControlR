using ControlR.DesktopClient.Common.Options;
using ControlR.DesktopClient.Common.Services;
using ControlR.DesktopClient.Common.Services.Encoders;
using ControlR.Libraries.Api.Contracts.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ControlR.DesktopClient.Common.Tests;

public class DesktopCapturerFactoryTests
{
  [Fact]
  public void FallbackNotice_ImageRequested_IsNotSet()
  {
    var factory = CreateFactory(CaptureEncoderType.Image, ffmpegPath: null, out var locator);

    Assert.Null(factory.FallbackNotice);
    locator.Verify(x => x.FindVp9Capable(), Times.Never);
  }

  [Fact]
  public void CreateNew_UnknownEncoder_ThrowsNotSupportedException()
  {
    var factory = CreateFactory((CaptureEncoderType)99, ffmpegPath: null, out _);

    Assert.Throws<NotSupportedException>(() => factory.CreateNew());
  }

  private static DesktopCapturerFactory CreateFactory(
    CaptureEncoderType encoderType,
    string? ffmpegPath,
    out Mock<IFfmpegLocator> locator)
  {
    locator = new Mock<IFfmpegLocator>();
    locator.Setup(x => x.FindVp9Capable()).Returns(ffmpegPath);

    return new DesktopCapturerFactory(
      new ServiceCollection().BuildServiceProvider(),
      locator.Object,
      Microsoft.Extensions.Options.Options.Create(new RemoteControlSessionOptions { EncoderType = encoderType }),
      NullLogger<DesktopCapturerFactory>.Instance);
  }
}
