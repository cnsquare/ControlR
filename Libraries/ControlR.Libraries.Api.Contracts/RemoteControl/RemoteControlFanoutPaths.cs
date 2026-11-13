namespace ControlR.Libraries.Api.Contracts.RemoteControl;

/// <summary>
/// The shared route for the shared remote-control (fan-out) WebSocket endpoint, used by the server
/// middleware that maps it, the desktop producer and the browser viewer that connect to it.
/// </summary>
public static class RemoteControlFanoutPaths
{
  public const string Path = "/remote-control-fanout";
}
