using Bitbound.SimpleMessenger;
using ControlR.DesktopClient.Common.Options;
using ControlR.DesktopClient.Common.ServiceInterfaces;
using ControlR.DesktopClient.Common.ServiceInterfaces.Toaster;
using ControlR.DesktopClient.Common.Services;
using ControlR.DesktopClient.Common.State;
using ControlR.Libraries.Api.Contracts.Dtos.RemoteControlDtos;
using ControlR.Libraries.Shared.Primitives;
using ControlR.Libraries.Shared.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace ControlR.DesktopClient.Tests;

public class RemoteControlViewerCommandHandlerTests
{
  [Fact]
  public async Task Handle_ClipboardTextRejectedByOperatingSystem_ReturnsThatFailure()
  {
    const string reason = "The clipboard is in use by another application.";
    var clipboardManager = new Mock<IClipboardManager>();
    clipboardManager
      .Setup(x => x.SetText("secret"))
      .ReturnsAsync(Result.Fail(reason));

    var result = await CreateSut(clipboardManager.Object)
      .Handle(CreateClipboardWrapper("secret"));

    Assert.False(result.IsSuccess);
    Assert.Equal(reason, result.Reason);
  }

  [Fact]
  public async Task Handle_ClipboardTextWritten_ReturnsSuccess()
  {
    var clipboardManager = new Mock<IClipboardManager>();
    clipboardManager
      .Setup(x => x.SetText("secret"))
      .ReturnsAsync(Result.Ok());

    var result = await CreateSut(clipboardManager.Object)
      .Handle(CreateClipboardWrapper("secret"));

    Assert.True(result.IsSuccess);
  }

  [Fact]
  public async Task Handle_ToggleAudio_AllowedSession_StartsAudioAndTellsTheUser()
  {
    var streamer = new FakeAudioStreamer();
    var toaster = new Mock<IToaster>();

    var result = await CreateSut(Mock.Of<IClipboardManager>(), streamer, toaster.Object, allowAudio: true)
      .Handle(CreateToggleAudioWrapper(true));

    Assert.True(result.IsSuccess);
    Assert.True(streamer.IsEnabled);
    toaster.Verify(
      x => x.ShowToast(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ToastIcon>()),
      Times.Once);
  }

  [Fact]
  public async Task Handle_ToggleAudio_SessionNotAllowed_NeverStartsAudio()
  {
    var streamer = new FakeAudioStreamer();
    var toaster = new Mock<IToaster>();

    await CreateSut(Mock.Of<IClipboardManager>(), streamer, toaster.Object, allowAudio: false)
      .Handle(CreateToggleAudioWrapper(true));

    Assert.False(streamer.IsEnabled);
    Assert.Equal(0, streamer.SetEnabledCount);
    toaster.Verify(
      x => x.ShowToast(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ToastIcon>()),
      Times.Never);
  }

  private static RemoteControlDtoWrapper CreateToggleAudioWrapper(bool isEnabled) =>
    RemoteControlDtoWrapper.Create(new ToggleAudioDto(isEnabled), RemoteControlDtoType.ToggleAudio);

  private static RemoteControlDtoWrapper CreateClipboardWrapper(string text) =>
    RemoteControlDtoWrapper.Create(new ClipboardTextDto(text, Guid.NewGuid()), RemoteControlDtoType.ClipboardText);

  private static RemoteControlViewerCommandHandler CreateSut(IClipboardManager clipboardManager) =>
    CreateSut(clipboardManager, new FakeAudioStreamer(), Mock.Of<IToaster>(), allowAudio: false);

  private static RemoteControlViewerCommandHandler CreateSut(
    IClipboardManager clipboardManager,
    IAudioStreamer audioStreamer,
    IToaster toaster,
    bool allowAudio) =>
    new(
      Mock.Of<IHostApplicationLifetime>(),
      audioStreamer,
      clipboardManager,
      new Mock<IDesktopCapturerFactory>().Object,
      Mock.Of<IDisplayManager>(),
      Mock.Of<IInputSimulator>(),
      Mock.Of<IMessenger>(),
      Mock.Of<IRemoteControlSessionState>(),
      Mock.Of<ISystemEnvironment>(),
      toaster,
      Options.Create(new RemoteControlSessionOptions { AllowAudio = allowAudio }),
      NullLogger<RemoteControlViewerCommandHandler>.Instance);

  private sealed class FakeAudioStreamer : IAudioStreamer
  {
    public bool IsEnabled { get; private set; }
    public int SetEnabledCount { get; private set; }

    public Result SetEnabled(bool isEnabled)
    {
      SetEnabledCount++;
      IsEnabled = isEnabled;
      return Result.Ok();
    }
  }
}
