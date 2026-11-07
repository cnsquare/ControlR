using ControlR.DesktopClient.ViewModels;

namespace ControlR.DesktopClient.Tests;

public class SessionIndicatorViewModelTests
{
  [Fact]
  public async Task EndSessionCommand_RunsTheCallback()
  {
    var ran = false;
    var viewModel = new SessionIndicatorWindowViewModel
    {
      ViewerName = "tech@example.com",
      OnEndSession = () =>
      {
        ran = true;
        return Task.CompletedTask;
      },
    };

    await ((CommunityToolkit.Mvvm.Input.AsyncRelayCommand)viewModel.EndSessionCommand).ExecuteAsync(null);

    Assert.True(ran);
    Assert.Equal("tech@example.com", viewModel.ViewerName);
  }

  [Fact]
  public async Task EndSessionCommand_NoCallback_DoesNotThrow()
  {
    var viewModel = new SessionIndicatorWindowViewModel();

    await ((CommunityToolkit.Mvvm.Input.AsyncRelayCommand)viewModel.EndSessionCommand).ExecuteAsync(null);
  }
}
