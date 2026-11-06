namespace ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1;

public record DeviceAlertDto(
  Guid Id,
  Guid DeviceId,
  string DeviceName,
  DeviceAlertKind Kind,
  string Message,
  DateTimeOffset RaisedAt);

public record AlertSettingsDto(
  int OfflineMinutes,
  int LowDiskPercent,
  string? Email,
  string? WebhookUrl);
