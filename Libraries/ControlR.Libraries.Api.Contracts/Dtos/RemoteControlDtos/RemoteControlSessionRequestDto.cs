using System.Text.Json.Serialization;

namespace ControlR.Libraries.Api.Contracts.Dtos.RemoteControlDtos;

[MessagePackObject(keyAsPropertyName: true)]
[method: JsonConstructor]
[method: SerializationConstructor]
public record RemoteControlSessionRequestDto(
  Guid SessionId,
  Uri WebsocketUri,
  int TargetSystemSession,
  int TargetProcessId,
  Guid DeviceId,
  bool NotifyUserOnSessionStart,
  bool RequireConsent)
{
  /// <summary>
  /// Set by the server from the server setting and the viewer's permissions. Any value a
  /// viewer supplies is overwritten, so this is what the desktop client can trust.
  /// </summary>
  public bool AllowAudio { get; init; }
  /// <summary>
  /// The encoder the viewer can decode. The desktop client uses image frames instead if it
  /// cannot provide this one, and a viewer draws whichever it receives.
  /// </summary>
  public CaptureEncoderType VideoEncoder { get; init; }
  /// <summary>
  /// The permissions the desktop client enforces on every viewer command. Set by the server,
  /// and any value a viewer supplies is overwritten.
  /// </summary>
  public string[] AllowedPermissions { get; init; } = [];
  public string? ViewerConnectionId { get; init; }
  public string? ViewerName { get; init; }
}