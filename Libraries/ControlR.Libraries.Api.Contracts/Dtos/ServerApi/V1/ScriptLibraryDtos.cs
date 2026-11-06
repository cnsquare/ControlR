using System.ComponentModel.DataAnnotations;
using ControlR.Libraries.Api.Contracts.Constants;

namespace ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1;

public record SaveSavedScriptRequestDto(
  [property: Required]
  [property: StringLength(ScriptLimits.MaxNameLength, MinimumLength = 1)]
  string Name,
  ScriptShell Shell,
  [property: Required]
  [property: StringLength(ScriptLimits.MaxContentLength, MinimumLength = 1)]
  string Content,
  [property: Range(ScriptLimits.MinTimeoutSeconds, ScriptLimits.MaxTimeoutSeconds)]
  int TimeoutSeconds = ScriptLimits.DefaultTimeoutSeconds);

public record SavedScriptDto(
  Guid Id,
  string Name,
  ScriptShell Shell,
  string Content,
  int TimeoutSeconds,
  string CreatedBy,
  DateTimeOffset CreatedAt);

public record CreateScriptScheduleRequestDto(
  [property: Required]
  [property: StringLength(ScriptLimits.MaxNameLength, MinimumLength = 1)]
  string Name,
  Guid SavedScriptId,
  [property: Required]
  [property: MinLength(1)]
  [property: MaxLength(DtoLimits.DeviceIdsMaxCount)]
  Guid[] DeviceIds,
  ScriptScheduleInterval Interval,
  DateTimeOffset? StartAt = null,
  bool RunOnNextConnect = false,
  [property: EmailAddress]
  [property: StringLength(256)]
  string? AlertEmail = null);

public record SetScriptScheduleEnabledRequestDto(bool IsEnabled);

public record ScriptScheduleDto(
  Guid Id,
  string Name,
  string ScriptName,
  ScriptShell Shell,
  ScriptScheduleInterval Interval,
  Guid[] DeviceIds,
  bool IsEnabled,
  DateTimeOffset NextRun,
  DateTimeOffset? LastRun,
  Guid? LastRunId,
  string? LastError,
  string CreatedBy,
  bool RunOnNextConnect,
  string? AlertEmail);

public record ScriptAlertDto(
  Guid Id,
  Guid ScheduleId,
  string ScheduleName,
  Guid? DeviceId,
  string? DeviceName,
  string Message,
  int Occurrences,
  DateTimeOffset LastOccurredAt,
  Guid? ScriptRunId);
