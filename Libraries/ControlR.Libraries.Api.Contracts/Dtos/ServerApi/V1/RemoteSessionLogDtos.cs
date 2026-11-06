namespace ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1;

public record RemoteSessionLogDto(
  Guid Id,
  Guid SessionId,
  Guid? DeviceId,
  string DeviceName,
  Guid UserId,
  string UserName,
  string? IpAddress,
  DateTimeOffset StartedAt,
  DateTimeOffset? EndedAt,
  string? FailureReason,
  bool WasInterrupted,
  RemoteSessionKind Kind,
  string? Detail);
