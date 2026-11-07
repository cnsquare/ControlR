using System.Windows.Input;

namespace ControlR.DesktopClient.ViewModels.Fakes;

public class SessionIndicatorWindowViewModelFake : ISessionIndicatorWindowViewModel
{
  public ICommand EndSessionCommand => new RelayCommand(() => { });
  public Func<Task>? OnEndSession { get; set; }
  public string ProductName => "ControlR";
  public string ViewerName { get; set; } = "a.technician@example.com";
}
