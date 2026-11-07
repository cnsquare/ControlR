using ControlR.DesktopClient.Common;
using ControlR.DesktopClient.Common.Options;
using ControlR.DesktopClient.Common.ServiceInterfaces;
using ControlR.DesktopClient.Common.ServiceInterfaces.Toaster;
using ControlR.Libraries.Api.Contracts.Dtos.IpcDtos;
using ControlR.Libraries.Avalonia.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ControlR.DesktopClient.Windows.Services;

public class WindowsRemoteControlHostBuilderFactory(
  IOptionsMonitor<DesktopClientOptions> desktopClientOptions,
  IUserInteractionService userInteractionService,
  IToaster toaster,
  ISessionIndicator sessionIndicator,
  IUiDispatcher dispatcher) : IRemoteControlHostBuilderFactory
{
  private readonly IOptionsMonitor<DesktopClientOptions> _desktopClientOptions = desktopClientOptions;
  private readonly IUiDispatcher _dispatcher = dispatcher;
  private readonly ISessionIndicator _sessionIndicator = sessionIndicator;
  private readonly IToaster _toaster = toaster;
  private readonly IUserInteractionService _userInteractionService = userInteractionService;

  public HostApplicationBuilder CreateHostBuilder(RemoteControlRequestIpcDto requestDto)
  {
    var builder = Host.CreateApplicationBuilder();

    builder.AddCommonRemoteControlServices(
      new RemoteControlHostDependencies
      {
        Toaster = _toaster,
        UserInteraction = _userInteractionService,
        SessionIndicator = _sessionIndicator,
        Dispatcher = _dispatcher
      },
      options =>
      {
        options.SessionId = requestDto.SessionId;
        options.AllowAudio = requestDto.AllowAudio;
        options.AllowedPermissions = (requestDto.AllowedPermissions ?? []).ToHashSet(StringComparer.Ordinal);
        options.EncoderType = requestDto.VideoEncoder;
        options.NotifyUser = requestDto.NotifyUserOnSessionStart;
        options.RequireConsent = requestDto.RequireConsent;
        options.ViewerName = requestDto.ViewerName;
        options.ViewerConnectionId = requestDto.ViewerConnectionId;
        options.WebSocketUri = requestDto.WebsocketUri;
      },
      options =>
      {
        options.InstanceId = _desktopClientOptions.CurrentValue.InstanceId;
      });

    builder.AddRemoteControlPlatformServices();
    builder.ConfigureRemoteControlHostValidation();
    return builder;
  }
}
