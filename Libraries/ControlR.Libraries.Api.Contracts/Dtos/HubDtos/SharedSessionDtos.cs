namespace ControlR.Libraries.Api.Contracts.Dtos.HubDtos;

/// <summary>
/// Asks the server to start a shared (fan-out) remote-control session for a device the caller is
/// already authorized to control, so additional invited technicians can watch the same stream.
/// <paramref name="RemoteControlSessionId"/> is the caller's own active remote-control session: the
/// device tees that session's already-encoded frames to the fan-out.
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
public record StartSharedSessionRequestDto(Guid DeviceId, Guid RemoteControlSessionId);

/// <summary>
/// Server → agent → device instruction to begin producing to a shared (fan-out) session. The device
/// finds the running host for <paramref name="RemoteControlSessionId"/> and connects to the fan-out
/// endpoint as the producer for <paramref name="SharedSessionId"/> using <paramref name="ProducerToken"/>.
/// The producer token is a secret and is never sent to any viewer.
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
public record StartSharedSessionProducerDto(Guid RemoteControlSessionId, Guid SharedSessionId, string ProducerToken);

/// <summary>
/// The viewer-facing result of starting a shared session. The inviting technician shares the
/// <see cref="SessionId"/> and <see cref="ViewerToken"/> with invited technicians, who connect to the
/// fan-out endpoint as viewers. The producer token is never returned to any viewer — it is delivered
/// only to the device that produces the stream.
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
public record StartSharedSessionResponseDto(Guid SessionId, string ViewerToken);

/// <summary>
/// Ends a shared (fan-out) remote-control session, disconnecting all invited viewers.
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
public record EndSharedSessionRequestDto(Guid SessionId);
