namespace ControlR.DesktopClient.Common.ServiceInterfaces;

/// <summary>
/// Shows a small, always-on-top window on the remote device while a remote control session is
/// active, so the person at the device can see that someone is connected and end the session.
/// It follows the user-notification setting, the same as Remotely: it appears for an unattended
/// session when notifications are on, and is hidden when they are off.
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
