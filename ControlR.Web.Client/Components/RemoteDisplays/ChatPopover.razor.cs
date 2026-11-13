using ControlR.Libraries.Api.Contracts.Dtos.Devices;
using Microsoft.AspNetCore.Components.Web;

namespace ControlR.Web.Client.Components.RemoteDisplays;

/// <summary>
/// Chat with the person at the device without leaving the remote-control screen. It reuses the shared
/// <see cref="IChatState"/> (the device-access layout already receives incoming messages into it), and
/// auto-targets the desktop session the remote-control session is controlling, so there is no separate
/// session picker.
/// </summary>
public partial class ChatPopover : DisposableComponent
{
  private Guid _chatStartedForRc = Guid.Empty;
  private MudTextField<string> _inputElement = null!;
  private ElementReference _messagesContainer;
  private string? _targetName;

  [Inject]
  public required IChatState ChatState { get; init; }

  [Inject]
  public required IDeviceState DeviceState { get; init; }

  [Inject]
  public required IJsInterop JsInterop { get; init; }

  [Inject]
  public required ILogger<ChatPopover> Logger { get; init; }

  [Inject]
  public required IRemoteControlState RemoteControlState { get; init; }

  [Inject]
  public required ISnackbar Snackbar { get; init; }

  [Inject]
  public required IHubConnection<IViewerHub> ViewerHub { get; init; }

  protected override async Task OnAfterRenderAsync(bool firstRender)
  {
    if (firstRender)
    {
      Disposables.Add(ChatState.OnStateChanged(() => InvokeAsync(StateHasChanged)));
      Disposables.Add(RemoteControlState.OnStateChanged(() => InvokeAsync(StateHasChanged)));
    }

    // Start (once per remote-control session) a chat targeting the session being controlled. This runs
    // after render rather than in OnInitialized so it also fires when the session connects later.
    var rc = RemoteControlState.CurrentSession;
    if (rc is not null && DeviceState.IsDeviceLoaded && _chatStartedForRc != rc.SessionId)
    {
      _chatStartedForRc = rc.SessionId;
      await EnsureChatStartedAsync(rc);
    }

    await base.OnAfterRenderAsync(firstRender);
  }

  private async Task EnsureChatStartedAsync(RemoteControlSession rc)
  {
    try
    {
      // Keep an existing conversation with the same target (e.g. one started from the Chat page).
      if (ChatState.CurrentSession is { } existing &&
          existing.SystemSessionId == rc.TargetSystemSession &&
          existing.ProcessId == rc.TargetProcessId &&
          ChatState.SessionId != Guid.Empty)
      {
        _targetName = existing.Username;
        return;
      }

      // Find the desktop session the remote-control session is controlling so chat reaches the same
      // user; fall back to a minimal target built from the session ids if the list lacks it.
      DesktopSession? target = null;
      var result = await ViewerHub.Server.GetActiveDesktopSessions2(new(DeviceState.CurrentDevice.Id));
      if (result.IsSuccess && result.Value?.Sessions is { } sessions)
      {
        target =
          sessions.FirstOrDefault(s => s.SystemSessionId == rc.TargetSystemSession && s.ProcessId == rc.TargetProcessId) ??
          sessions.FirstOrDefault(s => s.SystemSessionId == rc.TargetSystemSession);
      }

      target ??= new DesktopSession
      {
        SystemSessionId = rc.TargetSystemSession,
        ProcessId = rc.TargetProcessId,
      };

      ChatState.Clear();
      ChatState.CurrentSession = target;
      ChatState.SessionId = Guid.NewGuid();
      _targetName = target.Username;
      await InvokeAsync(StateHasChanged);
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Error starting chat from the remote-control screen.");
    }
  }

  private async Task HandleInputKeyDown(KeyboardEventArgs args)
  {
    if (args.Key != "Enter")
    {
      return;
    }

    if (ChatState.EnableMultiline && args.CtrlKey)
    {
      await SendMessage();
    }
    else if (!ChatState.EnableMultiline)
    {
      await SendMessage();
    }
  }

  private async Task SendMessage()
  {
    if (string.IsNullOrWhiteSpace(ChatState.NewMessage) || ChatState.CurrentSession is null)
    {
      return;
    }

    try
    {
      var newMessage = ChatState.NewMessage.Trim();

      var chatDto = new ChatMessageHubDto(
        DeviceState.CurrentDevice.Id,
        ChatState.SessionId,
        newMessage,
        string.Empty, // SenderName set in the hub
        string.Empty, // SenderEmail set in the hub
        ChatState.CurrentSession.SystemSessionId,
        ChatState.CurrentSession.ProcessId,
        DateTimeOffset.Now,
        Kind: ChatMessageKind.TextSend);

      var chatMessage = new ChatMessage
      {
        Message = newMessage,
        SenderName = "You",
        Timestamp = DateTimeOffset.Now,
        IsFromViewer = true,
        Kind = ChatMessageKind.TextSend,
      };

      ChatState.ChatMessages.Add(chatMessage);
      ChatState.NewMessage = string.Empty;

      var result = await ViewerHub.Server.SendChatMessage2(chatDto);
      if (!result.IsSuccess)
      {
        Logger.LogError("Failed to send chat message: {Error}", result.Reason);
        Snackbar.Add("Failed to send message", Severity.Error);
        _ = ChatState.ChatMessages.Remove(chatMessage);
        ChatState.NewMessage = newMessage;
      }

      await InvokeAsync(StateHasChanged);
      await _inputElement.FocusAsync();
      await JsInterop.ScrollToEnd(_messagesContainer);
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Error sending chat message.");
      Snackbar.Add("Error sending message", Severity.Error);
    }
  }
}
