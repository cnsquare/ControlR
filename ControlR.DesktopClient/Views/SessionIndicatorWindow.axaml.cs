using Avalonia;
using Avalonia.Controls;

namespace ControlR.DesktopClient.Views;

public partial class SessionIndicatorWindow : Window
{
  public SessionIndicatorWindow()
  {
    InitializeComponent();
  }

  protected override void OnOpened(EventArgs e)
  {
    base.OnOpened(e);
    PositionAtTopCenter();
  }

  private void PositionAtTopCenter()
  {
    try
    {
      // Top-center, clear of the toast window, which sits at the bottom-right.
      var screen = Screens.Primary;
      if (screen?.WorkingArea is { } workingArea)
      {
        var x = workingArea.X + (workingArea.Width - (ClientSize.Width * screen.Scaling)) / 2;
        var y = workingArea.Y + (12 * screen.Scaling);
        Position = new PixelPoint((int)x, (int)y);
      }
    }
    catch
    {
      // Keep the default position if the screen cannot be read.
    }
  }
}
