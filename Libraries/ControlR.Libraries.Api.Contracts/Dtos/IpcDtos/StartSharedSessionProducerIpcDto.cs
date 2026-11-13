namespace ControlR.Libraries.Api.Contracts.Dtos.IpcDtos;

/// <summary>
/// Agent → desktop-client instruction to begin producing an already-running remote-control session's
/// frames to a shared (fan-out) session. The desktop finds the host for
/// <see cref="RemoteControlSessionId"/> and connects to the fan-out endpoint as the producer for
/// <see cref="SharedSessionId"/> with <see cref="ProducerToken"/>.
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
public record StartSharedSessionProducerIpcDto(
  Guid RemoteControlSessionId,
  Guid SharedSessionId,
  string ProducerToken);
