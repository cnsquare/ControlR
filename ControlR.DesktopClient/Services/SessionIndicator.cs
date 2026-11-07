using ControlR.Libraries.Avalonia.Services;
using Microsoft.Extensions.Logging;

namespace ControlR.DesktopClient.Services;

/// <summary>
/// Shows the always-on-top session indicator window for the life of a remote control session.
/// </summary>
public sealed class SessionIndicator(
  IUiDispatcher dispatcher,
  ILogger<SessionIndicator> logger) : ISessionIndicator
{
  private readonly IUiDispatcher _dispatcher = dispatcher;
  private readonly ILogger<SessionIndicator> _logger = logger;

  private SessionIndicatorWindow? _window;

  public void Hide()
  {
    _dispatcher.Post(() =>
    {
      try
      {
        _window?.Close();
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Failed to close the session indicator window.");
      }
      finally
      {
        _window = null;
      }
    });
  }

  public void Show(string viewerName, Func<Task> onEndSession)
  {
    _dispatcher.Post(() =>
    {
      try
      {
        // A session should only ever have one indicator, so a lingering one is replaced.
        _window?.Close();

        var viewModel = StaticServiceProvider.Instance.GetRequiredService<ISessionIndicatorWindowViewModel>();
        viewModel.ViewerName = viewerName;
        viewModel.OnEndSession = async () =>
        {
          try
          {
            await onEndSession();
          }
          catch (Exception ex)
          {
            _logger.LogError(ex, "Error while ending the session from the indicator.");
          }
        };

        _window = StaticServiceProvider.Instance.GetRequiredService<SessionIndicatorWindow>();
        _window.DataContext = viewModel;
        _window.Show();
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Failed to show the session indicator window.");
      }
    });
  }
}
