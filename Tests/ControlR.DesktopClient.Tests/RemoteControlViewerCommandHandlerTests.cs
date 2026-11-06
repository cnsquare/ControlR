using Bitbound.SimpleMessenger;
using ControlR.DesktopClient.Common.Options;
using ControlR.DesktopClient.Common.ServiceInterfaces;
using ControlR.DesktopClient.Common.ServiceInterfaces.Toaster;
using ControlR.DesktopClient.Common.Services;
using ControlR.DesktopClient.Common.State;
using ControlR.Libraries.Api.Contracts.Authz;
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

  [Theory]
  [InlineData(RemoteControlDtoType.ClipboardText)]
  [InlineData(RemoteControlDtoType.KeyEvent)]
  [InlineData(RemoteControlDtoType.MouseClick)]
  [InlineData(RemoteControlDtoType.MovePointer)]
  [InlineData(RemoteControlDtoType.RequestClipboardText)]
  [InlineData(RemoteControlDtoType.ToggleBlockInput)]
  [InlineData(RemoteControlDtoType.TogglePrivacyScreen)]
  [InlineData(RemoteControlDtoType.TypeText)]
  [InlineData(RemoteControlDtoType.WheelScroll)]
  public async Task Handle_ViewOnlySession_RefusesEveryCommandThatActsOnTheDevice(RemoteControlDtoType dtoType)
  {
    var sut = CreateSut(Mock.Of<IClipboardManager>(), PermissionNames.DeviceRemoteControlConnect);
    var wrapper = RemoteControlDtoWrapper.Create(new ToggleAudioDto(true), dtoType);

    var result = await sut.Handle(wrapper);

    Assert.False(result.IsSuccess);
    Assert.Contains("permission", result.Reason);
  }

  [Fact]
  public async Task Handle_ClipboardWriteWithoutPermission_NeverTouchesTheClipboard()
  {
    var clipboardManager = new Mock<IClipboardManager>();
    var sut = CreateSut(
      clipboardManager.Object,
      PermissionNames.DeviceRemoteControlConnect,
      PermissionNames.DeviceRemoteControlInteract);

    var result = await sut.Handle(CreateClipboardWrapper("secret"));

    Assert.False(result.IsSuccess);
    clipboardManager.Verify(x => x.SetText(It.IsAny<string>()), Times.Never);
  }

  [Fact]
  public async Task Handle_KeyEventWithoutInteract_NeverReachesInputSimulator()
  {
    var inputSimulator = new Mock<IInputSimulator>(MockBehavior.Strict);
    var sut = CreateSut(
      Mock.Of<IClipboardManager>(),
      new FakeAudioStreamer(),
      Mock.Of<IToaster>(),
      allowAudio: false,
      inputSimulator.Object,
      [PermissionNames.DeviceRemoteControlConnect]);
    var wrapper = RemoteControlDtoWrapper.Create(
      new ToggleAudioDto(true),
      RemoteControlDtoType.KeyEvent);

    var result = await sut.Handle(wrapper);

    Assert.False(result.IsSuccess);
    inputSimulator.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task Handle_SessionGrantedNothing_RefusesEvenClosingCommands()
  {
    var sut = CreateSut(Mock.Of<IClipboardManager>(), []);

    var result = await sut.Handle(RemoteControlDtoWrapper.Create(
      new ToggleAudioDto(true),
      RemoteControlDtoType.RequestKeyFrame));

    Assert.False(result.IsSuccess);
  }

  [Fact]
  public void GetRequiredPermission_LetsViewOnlyViewersChangeWhatTheySee()
  {
    Assert.Equal(
      PermissionNames.DeviceRemoteControlConnect,
      RemoteControlCommandPermissions.GetRequiredPermission(RemoteControlDtoType.ChangeDisplays));
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

  private static readonly string[] _allPermissions =
  [
    PermissionNames.DeviceClipboardRead,
    PermissionNames.DeviceClipboardWrite,
    PermissionNames.DeviceRemoteControlAudio,
    PermissionNames.DeviceRemoteControlBlockInput,
    PermissionNames.DeviceRemoteControlConnect,
    PermissionNames.DeviceRemoteControlInteract,
    PermissionNames.DeviceRemoteControlPrivacyScreen,
  ];

  private static RemoteControlViewerCommandHandler CreateSut(IClipboardManager clipboardManager) =>
    CreateSut(clipboardManager, _allPermissions);

  private static RemoteControlViewerCommandHandler CreateSut(
    IClipboardManager clipboardManager,
    params string[] allowedPermissions) =>
    CreateSut(
      clipboardManager,
      new FakeAudioStreamer(),
      Mock.Of<IToaster>(),
      allowAudio: false,
      Mock.Of<IInputSimulator>(),
      allowedPermissions);

  private static RemoteControlViewerCommandHandler CreateSut(
    IClipboardManager clipboardManager,
    IAudioStreamer audioStreamer,
    IToaster toaster,
    bool allowAudio) =>
    CreateSut(clipboardManager, audioStreamer, toaster, allowAudio, Mock.Of<IInputSimulator>(), _allPermissions);

  private static RemoteControlViewerCommandHandler CreateSut(
    IClipboardManager clipboardManager,
    IAudioStreamer audioStreamer,
    IToaster toaster,
    bool allowAudio,
    IInputSimulator inputSimulator,
    string[] allowedPermissions) =>
    new(
      Mock.Of<IHostApplicationLifetime>(),
      audioStreamer,
      clipboardManager,
      new Mock<IDesktopCapturerFactory>().Object,
      Mock.Of<IDisplayManager>(),
      inputSimulator,
      Mock.Of<IMessenger>(),
      Mock.Of<IRemoteControlSessionState>(),
      Mock.Of<ISystemEnvironment>(),
      toaster,
      Options.Create(new RemoteControlSessionOptions
      {
        AllowAudio = allowAudio,
        AllowedPermissions = allowedPermissions.ToHashSet(StringComparer.Ordinal),
      }),
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
