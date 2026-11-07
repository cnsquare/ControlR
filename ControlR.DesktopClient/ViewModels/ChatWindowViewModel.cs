using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using ControlR.DesktopClient.Common.ViewModels;
using ControlR.DesktopClient.Models;
using ControlR.Libraries.Api.Contracts.Enums;
using Microsoft.Extensions.Logging;

namespace ControlR.DesktopClient.ViewModels;

public interface IChatWindowViewModel
{
  event EventHandler? MessagesChanged;
  
  string ChatTitle { get; }
  ICommand DecreaseOpacityCommand { get; }
  ICommand IncreaseOpacityCommand { get; }
  bool IsAlwaysOnTop { get; set; }
  bool IsTransparent { get; set; }
  ObservableCollection<ChatMessageViewModel> Messages { get; }
  string NewMessage { get; set; }
  ICommand SendMessageCommand { get; }
  ChatSession? Session { get; set; }
  double WindowOpacity { get; }

  Task HandleChatWindowClosed();

  Task SendMessage();
}

public class ChatWindowViewModel(
  IChatSessionManager chatSessionManager,
  IToaster toaster,
  ILogger<ChatWindowViewModel> logger) : ViewModelBase<ChatWindow>, IChatWindowViewModel
{
  /// <summary>
  /// The floor keeps the window readable, so a chat can never be made effectively invisible.
  /// </summary>
  public const double MinimumOpacity = 0.3;

  private const double FullOpacity = 1;
  private const double OpacityStep = 0.1;
  private const double TransparentOpacity = 0.6;

  private readonly IChatSessionManager _chatSessionManager = chatSessionManager;
  private readonly ILogger<ChatWindowViewModel> _logger = logger;
  private readonly IToaster _toaster = toaster;

  private bool _isAlwaysOnTop;
  private bool _isTransparent;
  private string _newMessage = string.Empty;
  private double _opacityBeforeTransparent = FullOpacity;
  private ChatSession? _session;
  private double _windowOpacity = FullOpacity;

  public event EventHandler? MessagesChanged;

  public string ChatTitle => Session is not null
    ? string.Format(Localization.ChatWindowTitle, Session.ViewerName)
    : Localization.ChatWindowDefaultTitle;

  public ObservableCollection<ChatMessageViewModel> Messages => 
    Session?.Messages ?? [];

  public ICommand DecreaseOpacityCommand => new RelayCommand(() => AdjustOpacity(-OpacityStep));

  public ICommand IncreaseOpacityCommand => new RelayCommand(() => AdjustOpacity(OpacityStep));

  public bool IsAlwaysOnTop
  {
    get => _isAlwaysOnTop;
    set => SetProperty(ref _isAlwaysOnTop, value);
  }

  public bool IsTransparent
  {
    get => _isTransparent;
    set
    {
      if (!SetProperty(ref _isTransparent, value))
      {
        return;
      }

      if (value)
      {
        _opacityBeforeTransparent = WindowOpacity;
        WindowOpacity = Math.Min(WindowOpacity, TransparentOpacity);
      }
      else
      {
        WindowOpacity = _opacityBeforeTransparent;
      }
    }
  }

  public string NewMessage
  {
    get => _newMessage;
    set => SetProperty(ref _newMessage, value);
  }

  public ICommand SendMessageCommand => new AsyncRelayCommand(SendMessage);

  public double WindowOpacity
  {
    get => _windowOpacity;
    private set => SetProperty(ref _windowOpacity, value);
  }

  public ChatSession? Session 
  { 
    get => _session;
    set
    {
      // Unsubscribe from old session's messages
      if (_session?.Messages is not null)
      {
        _session.Messages.CollectionChanged -= OnMessagesCollectionChanged;
      }

      if (SetProperty(ref _session, value))
      {
        OnPropertyChanged(nameof(ChatTitle));
        OnPropertyChanged(nameof(Messages));

        // Subscribe to new session's messages
        if (_session?.Messages is not null)
        {
          _session.Messages.CollectionChanged += OnMessagesCollectionChanged;
        }

        // Notify that messages collection changed
        MessagesChanged?.Invoke(this, EventArgs.Empty);
      }
    }
  }

  private void AdjustOpacity(double change)
  {
    // Fine adjustment takes over from the toggle, so the two never disagree.
    _isTransparent = false;
    OnPropertyChanged(nameof(IsTransparent));

    WindowOpacity = Math.Clamp(Math.Round(WindowOpacity + change, 1), MinimumOpacity, FullOpacity);
  }

  public async Task HandleChatWindowClosed()
  {
    if (Session is null)
    {
      return;
    }

    // Send a system message to notify the viewer that the chat window was closed
    var systemMessage = Localization.ChatWindowClosedSystemMessage;
    var success = await _chatSessionManager.SendResponse(
      Session.SessionId,
      systemMessage,
      ChatMessageKind.WindowClose);

    await _chatSessionManager.CloseChatSession(Session.SessionId, false);

    if (success)
    {
      _logger.LogDebug("Chat window closed system message sent for session {SessionId}", Session.SessionId);
    }
    else
    {
      _logger.LogWarning("Failed to send chat window closed system message for session {SessionId}", Session.SessionId);
    }
  }

  public async Task SendMessage()
  {
    if (Session is null || string.IsNullOrWhiteSpace(NewMessage))
    {
      return;
    }

    var message = NewMessage.Trim();

    // Send the response via the chat session manager
    var success = await _chatSessionManager.SendResponse(
      Session.SessionId,
      message,
      ChatMessageKind.TextSend);

    if (success)
    {
      // Add the message to our local collection immediately
      var messageViewModel = new ChatMessageViewModel(message, Localization.You, false);
      Messages.Add(messageViewModel);
      NewMessage = string.Empty;
      _logger.LogDebug("Message sent successfully for session {SessionId}", Session.SessionId);
    }
    else
    {
      _logger.LogWarning("Failed to send message for session {SessionId}", Session.SessionId);
      await _toaster.ShowToast(
        Localization.ChatMessageSendFailureTitle, 
        Localization.ChatMessageSendFailureMessage, 
        ToastIcon.Error);
    }
  }

  private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
  {
    // Forward the collection changed event to our MessagesChanged event
    MessagesChanged?.Invoke(this, EventArgs.Empty);
  }
}