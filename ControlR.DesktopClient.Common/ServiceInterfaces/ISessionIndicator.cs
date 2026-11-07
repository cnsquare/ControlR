namespace ControlR.DesktopClient.Common.ServiceInterfaces;

/// <summary>
/// Shows a small, always-on-top window on the remote device for the whole time a remote control
/// session is active, so the person at the device can always see that someone is connected. This
/// is shown regardless of the notification setting, and the person cannot be connected to without
/// it appearing.
/// </summary>
public interface ISessionIndicator
{
  /// <param name="viewerName">Who is connected, shown to the person at the device.</param>
  /// <param name="onEndSession">Runs when the person clicks End Session on the indicator.</param>
  void Show(string viewerName, Func<Task> onEndSession);

  void Hide();
}

/// <summary>
/// Used by hosts that have no desktop UI, such as a headless platform. It shows nothing, which is
/// why those platforms keep their own notification and consent behaviour instead.
/// </summary>
public sealed class NoOpSessionIndicator : ISessionIndicator
{
  public void Hide()
  {
  }

  public void Show(string viewerName, Func<Task> onEndSession)
  {
  }
}
