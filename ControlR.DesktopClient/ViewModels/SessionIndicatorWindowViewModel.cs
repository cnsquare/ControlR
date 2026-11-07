using System.Windows.Input;
using ControlR.Libraries.Branding;

namespace ControlR.DesktopClient.ViewModels;

public interface ISessionIndicatorWindowViewModel
{
  ICommand EndSessionCommand { get; }
  Func<Task>? OnEndSession { get; set; }
  string ProductName { get; }
  string ViewerName { get; set; }
}

public partial class SessionIndicatorWindowViewModel : ObservableObject, ISessionIndicatorWindowViewModel
{
  [ObservableProperty]
  private string _viewerName = string.Empty;

  public ICommand EndSessionCommand => new AsyncRelayCommand(RunEndSession);

  /// <summary>
  /// Set by the service when the window is shown. Nothing happens if it was not.
  /// </summary>
  public Func<Task>? OnEndSession { get; set; }

  public string ProductName => BrandingConstants.BrandName;

  private async Task RunEndSession()
  {
    if (OnEndSession is { } callback)
    {
      await callback();
    }
  }
}
