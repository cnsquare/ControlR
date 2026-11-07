namespace ControlR.DesktopClient.Services;

internal sealed class ShellNavigationItemProvider : INavigationItemProvider
{
  public IEnumerable<NavigationItemDescriptor> GetNavigationItems()
  {
    // The managed client has no active-connections list, matching Remotely's unattended mode.
    // An active session shows through the session indicator instead, when notifications are on.
    return
    [
      new(typeof(ISettingsViewModel), "settings_regular", Localization.Settings, 200),
      new(typeof(IAboutViewModel), "question_circle_regular", Localization.About, 300)
    ];
  }
}
