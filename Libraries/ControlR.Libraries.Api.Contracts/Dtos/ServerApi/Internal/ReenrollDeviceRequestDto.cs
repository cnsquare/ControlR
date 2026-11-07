using System.ComponentModel.DataAnnotations;

using ControlR.Libraries.Api.Contracts.Dtos.HubDtos;

namespace ControlR.Libraries.Api.Contracts.Dtos.ServerApi.Internal;

/// <summary>
/// Sent by an agent the server no longer knows, to re-create its device record after a database
/// loss. The fleet secret is the only authorization: it lives in the server's configuration, not
/// its database, so it survives a rebuild. The agent keeps its existing identity (device id and key).
/// </summary>
public record ReenrollDeviceRequestDto(
  [property: Required]
  DeviceUpdateRequestDto Device,
  [property: Required]
  string PublicKey,
  [property: Required]
  string FleetSecret);
