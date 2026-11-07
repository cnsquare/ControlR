namespace ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1.Recordings;

public record RecordingResponseDto(
  Guid Id,
  Guid? DeviceId,
  string? DeviceName,
  string? CreatedByUserName,
  string ContentType,
  long SizeBytes,
  int? DurationSeconds,
  DateTimeOffset CreatedAt);
