using ControlR.Assist.Services;
using ControlR.Assist.ViewModels;
using ControlR.Libraries.Api.Contracts.Dtos;
using ControlR.Libraries.Api.Contracts.Dtos.HubDtos;
using ControlR.Libraries.Api.Contracts.Hubs;
using ControlR.Libraries.Avalonia.Theming;
using ControlR.Libraries.Shared.Primitives;
using ControlR.Libraries.Shared.Services.Processes;
using ControlR.Libraries.Signalr.Client;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ControlR.Assist.Tests;

/// <summary>
/// Validates the Settings "Allow Access" (unattended access) button end to end: executing the button's
/// command on the Settings view model must run the shared Home view model's setup flow and reach the
/// installer. This is the path that broke when the button was moved from Home to Settings.
/// </summary>
public class AllowAccessButtonTests
{
  [Fact]
  public async Task AllowAccessCommand_DuringActiveSession_RunsFlowAndInvokesInstaller()
  {
    var hub = new GrantingHub();
    var installer = new Mock<IUnattendedAccessInstaller>();
    installer
      .Setup(x => x.Install(It.IsAny<Uri>(), It.IsAny<UnattendedAccessGrantDto>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(Result.Ok());

    var coordinator = new AssistSessionCoordinator(
      hub,
      new InputGateHostFactory(),
      new FakeSettingsService(),
      NullLogger<AssistSessionCoordinator>.Instance);

    // Drive the session to Active so TrySetInputSuspended and RequestUnattendedAccess both succeed.
    Assert.True(await RunWithinTimeout(coordinator.ConnectAsync("server.example.com", CancellationToken.None)));
    await RunWithinTimeout(coordinator.HandleSessionStarted(
      new(hub.LastSessionId, "token", "Jane", "jane@example.com")));
    await WaitForStateAsync(coordinator, AssistSessionState.Active);

    var home = new HomeViewModel(
      coordinator,
      new FakeSettingsService(),
      Mock.Of<INavigationService>(),
      installer.Object,
      NullLogger<HomeViewModel>.Instance)
    {
      ServerUrl = "server.example.com"
    };

    var settings = new SettingsViewModel(
      new FakeSettingsService(),
      NullLogger<SettingsViewModel>.Instance,
      Mock.Of<IProcessManager>(),
      Mock.Of<INavigationService>(),
      Mock.Of<IThemeProvider>(),
      home);

    // This is exactly what the Settings button's Command binding invokes.
    await RunWithinTimeout(settings.AllowAccessCommand.ExecuteAsync(null));

    installer.Verify(
      x => x.Install(It.IsAny<Uri>(), It.IsAny<UnattendedAccessGrantDto>(), It.IsAny<CancellationToken>()),
      Times.Once);
    Assert.Equal(Localization.UnattendedAccessInstalled, home.UnattendedAccessStatus);

    await RunWithinTimeout(coordinator.DisposeAsync());
  }

  private static async Task<T> RunWithinTimeout<T>(Task<T> task)
  {
    var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(30)));
    Assert.True(ReferenceEquals(completed, task), "The operation did not complete within the timeout.");
    return await task;
  }

  private static async Task RunWithinTimeout(Task task)
  {
    var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(30)));
    Assert.True(ReferenceEquals(completed, task), "The operation did not complete within the timeout.");
    await task;
  }

  private static async Task RunWithinTimeout(ValueTask valueTask) => await RunWithinTimeout(valueTask.AsTask());

  private static async Task WaitForStateAsync(AssistSessionCoordinator controller, AssistSessionState expected)
  {
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
    while (controller.State != expected && DateTime.UtcNow < deadline)
    {
      await Task.Delay(25);
    }

    Assert.Equal(expected, controller.State);
  }

  private sealed class InputGateHostFactory : IAssistSessionHostFactory
  {
    public IHost CreateSessionHost(AssistSessionRequest request)
    {
      var builder = Host.CreateApplicationBuilder();
      // The real session host registers this; the coordinator needs it to suspend input.
      builder.Services.AddSingleton<AssistInputGate>();
      return builder.Build();
    }
  }

  private sealed class FakeSettingsService : IAssistSettingsService
  {
    public string? GetLanguage() => null;
    public AssistAppSettings GetSettings() => new();
    public AvaloniaThemeMode GetThemeMode() => AvaloniaThemeMode.System;
    public Task SaveAsync(AssistAppSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
    public void SetLanguage(string? language) { }
    public void SetThemeMode(AvaloniaThemeMode mode) { }
  }

  private sealed class GrantingHub : IHubConnection<IAssistHub>, IAssistHub
  {
    private Guid _lastSessionId = Guid.NewGuid();

    event Func<Exception?, Task>? IHubConnection<IAssistHub>.Closed { add { } remove { } }
    event Func<Exception, Task>? IHubConnection<IAssistHub>.ConnectThrew { add { } remove { } }
    event Func<string?, Task>? IHubConnection<IAssistHub>.Reconnected { add { } remove { } }
    event Func<Exception?, Task>? IHubConnection<IAssistHub>.Reconnecting { add { } remove { } }

    public string? ConnectionId => "test-connection";
    public HubConnectionState ConnectionState => HubConnectionState.Connected;
    public bool IsConnected => true;
    public Guid LastSessionId => _lastSessionId;
    public IAssistHub Server => this;

    public Task<HubResult> CancelAssistConsent(CancelAssistConsentRequestDto request) =>
      Task.FromResult(HubResult.Ok());

    public Task<bool> Connect(
      Uri hubEndpoint,
      bool autoRetry,
      Action<HttpConnectionOptions>? configure = null,
      CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<HubResult<CreateAssistSessionResponseDto>> CreateAssistSession()
    {
      _lastSessionId = Guid.NewGuid();
      return Task.FromResult(HubResult.Ok(new CreateAssistSessionResponseDto("123456789", _lastSessionId)));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task<HubResult> EndAssistSession(EndAssistSessionRequestDto request) =>
      Task.FromResult(HubResult.Ok());

    public Task<HubResult<JoinAssistSessionResponseDto>> JoinAssistSession(JoinAssistSessionRequestDto request) =>
      Task.FromResult(HubResult.Fail<JoinAssistSessionResponseDto>("The assist app never joins a session."));

    public Task<HubResult<UnattendedAccessGrantDto>> RequestUnattendedAccess(RequestUnattendedAccessRequestDto request) =>
      Task.FromResult(HubResult.Ok(new UnattendedAccessGrantDto(Guid.NewGuid(), Guid.NewGuid(), "key-secret")));

    public Task<HubResult> RespondToAssistConsent(RespondToAssistConsentRequestDto request) =>
      Task.FromResult(HubResult.Ok());

    public Task Send(string methodName, object?[] args, CancellationToken cancellationToken = default) =>
      Task.CompletedTask;

    public Task WaitForConnected(CancellationToken cancellationToken) => Task.CompletedTask;
  }
}
