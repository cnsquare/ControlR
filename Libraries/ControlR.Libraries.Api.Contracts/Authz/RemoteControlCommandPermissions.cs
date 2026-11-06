using ControlR.Libraries.Api.Contracts.Dtos.RemoteControlDtos;

namespace ControlR.Libraries.Api.Contracts.Authz;

/// <summary>
/// Map from a viewer command to the device permission it requires. The desktop client enforces
/// this against the permissions the server granted the session, because commands reach it over
/// the relay without passing through the server. The UI checks the same permissions per control.
/// </summary>
public static class RemoteControlCommandPermissions
{
  public static string GetRequiredPermission(RemoteControlDtoType dtoType) => dtoType switch
  {
    RemoteControlDtoType.ClipboardText => PermissionNames.DeviceClipboardWrite,
    RemoteControlDtoType.RequestClipboardText => PermissionNames.DeviceClipboardRead,
    RemoteControlDtoType.ToggleBlockInput => PermissionNames.DeviceRemoteControlBlockInput,
    RemoteControlDtoType.TogglePrivacyScreen => PermissionNames.DeviceRemoteControlPrivacyScreen,
    RemoteControlDtoType.ToggleAudio => PermissionNames.DeviceRemoteControlAudio,
    RemoteControlDtoType.ChangeDisplays
      or RemoteControlDtoType.RequestKeyFrame
      or RemoteControlDtoType.UpdateCaptureSettings
      or RemoteControlDtoType.CloseRemoteControlSession => PermissionNames.DeviceRemoteControlConnect,
    _ => PermissionNames.DeviceRemoteControlInteract
  };
}
