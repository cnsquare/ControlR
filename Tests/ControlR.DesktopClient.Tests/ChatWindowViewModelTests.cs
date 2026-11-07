using ControlR.DesktopClient.Common.ServiceInterfaces;
using ControlR.DesktopClient.Common.ServiceInterfaces.Toaster;
using ControlR.DesktopClient.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ControlR.DesktopClient.Tests;

public class ChatWindowViewModelTests
{
  [Fact]
  public void NewWindow_IsOpaqueAndNotOnTop()
  {
    var viewModel = CreateViewModel();

    Assert.Equal(1, viewModel.WindowOpacity);
    Assert.False(viewModel.IsTransparent);
    Assert.False(viewModel.IsAlwaysOnTop);
  }

  [Fact]
  public void IsTransparent_TurnedOffAgain_RestoresTheOpacity()
  {
    var viewModel = CreateViewModel();
    viewModel.DecreaseOpacityCommand.Execute(null);

    viewModel.IsTransparent = true;
    Assert.Equal(0.6, viewModel.WindowOpacity);

    viewModel.IsTransparent = false;
    Assert.Equal(0.9, viewModel.WindowOpacity);
  }

  [Fact]
  public void IsTransparent_AlreadyBelowItsLevel_DoesNotRaiseTheOpacity()
  {
    var viewModel = CreateViewModel();
    for (var i = 0; i < 6; i++)
    {
      viewModel.DecreaseOpacityCommand.Execute(null);
    }

    viewModel.IsTransparent = true;

    Assert.Equal(0.4, viewModel.WindowOpacity);
  }

  [Fact]
  public void DecreaseOpacity_NeverGoesBelowTheMinimum()
  {
    var viewModel = CreateViewModel();

    for (var i = 0; i < 20; i++)
    {
      viewModel.DecreaseOpacityCommand.Execute(null);
    }

    Assert.Equal(ChatWindowViewModel.MinimumOpacity, viewModel.WindowOpacity);
  }

  [Fact]
  public void IncreaseOpacity_NeverGoesAboveFull()
  {
    var viewModel = CreateViewModel();

    viewModel.IncreaseOpacityCommand.Execute(null);

    Assert.Equal(1, viewModel.WindowOpacity);
  }

  [Fact]
  public void AdjustingOpacity_ReleasesTheToggle()
  {
    var viewModel = CreateViewModel();
    viewModel.IsTransparent = true;

    viewModel.IncreaseOpacityCommand.Execute(null);

    Assert.False(viewModel.IsTransparent);
    Assert.Equal(0.7, viewModel.WindowOpacity);
  }

  private static ChatWindowViewModel CreateViewModel()
  {
    return new ChatWindowViewModel(
      Mock.Of<IChatSessionManager>(),
      Mock.Of<IToaster>(),
      NullLogger<ChatWindowViewModel>.Instance);
  }
}
