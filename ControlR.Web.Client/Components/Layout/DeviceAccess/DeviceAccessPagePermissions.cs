namespace ControlR.Web.Client.Components.Layout.DeviceAccess;

internal static class DeviceAccessPagePermissions
{
  internal static IReadOnlyList<string> AllRoutes { get; } =
  [
    ClientRoutes.DeviceAccess,
    ClientRoutes.DeviceAccessRemoteControl,
    ClientRoutes.DeviceAccessTerminal,
    ClientRoutes.DeviceAccessScripts,
    ClientRoutes.DeviceAccessChat,
    ClientRoutes.DeviceAccessFileSystem,
    ClientRoutes.DeviceAccessRemoteLogs,
    ClientRoutes.DeviceAccessVncRelay,
  ];

  public static bool CanAccess(DeviceAccessPermissionsDto? permissions, string path)
  {
    if (permissions is null)
    {
      return false;
    }

    return path switch
    {
      ClientRoutes.DeviceAccess => permissions.CanViewOverview,
      ClientRoutes.DeviceAccessRemoteControl => permissions.CanUseRemoteControl,
      ClientRoutes.DeviceAccessTerminal => permissions.CanUseTerminal,
      ClientRoutes.DeviceAccessScripts => permissions.CanRunScripts,
      ClientRoutes.DeviceAccessChat => permissions.CanUseChat,
      ClientRoutes.DeviceAccessFileSystem => permissions.CanReadFileSystem,
      ClientRoutes.DeviceAccessRemoteLogs => permissions.CanReadLogs,
      ClientRoutes.DeviceAccessVncRelay => permissions.CanUseVncRelay,
      _ => false
    };
  }

  public static string? FirstAllowedRoute(DeviceAccessPermissionsDto? permissions)
  {
    if (permissions is null)
    {
      return null;
    }

    var routes = new (string Route, bool Allowed)[]
    {
      (ClientRoutes.DeviceAccess, permissions.CanViewOverview),
      (ClientRoutes.DeviceAccessRemoteControl, permissions.CanUseRemoteControl),
      (ClientRoutes.DeviceAccessTerminal, permissions.CanUseTerminal),
      (ClientRoutes.DeviceAccessScripts, permissions.CanRunScripts),
      (ClientRoutes.DeviceAccessChat, permissions.CanUseChat),
      (ClientRoutes.DeviceAccessFileSystem, permissions.CanReadFileSystem),
      (ClientRoutes.DeviceAccessRemoteLogs, permissions.CanReadLogs),
      (ClientRoutes.DeviceAccessVncRelay, permissions.CanUseVncRelay)
    };

    return routes.FirstOrDefault(x => x.Allowed).Route;
  }
}