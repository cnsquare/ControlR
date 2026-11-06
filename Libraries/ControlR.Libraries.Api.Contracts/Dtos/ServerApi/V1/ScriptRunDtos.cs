using System.ComponentModel.DataAnnotations;
using ControlR.Libraries.Api.Contracts.Constants;

namespace ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1;

public record CreateScriptRunRequestDto(
  [property: Required]
  [property: MinLength(1)]
  [property: MaxLength(DtoLimits.DeviceIdsMaxCount)]
  Guid[] DeviceIds,
  ScriptShell Shell,
  [property: Required]
  [property: StringLength(ScriptLimits.MaxContentLength, MinimumLength = 1)]
  string Content,
  [property: Range(ScriptLimits.MinTimeoutSeconds, ScriptLimits.MaxTimeoutSeconds)]
  int TimeoutSeconds = ScriptLimits.DefaultTimeoutSeconds);

public record ScriptResultDto(
  Guid Id,
  Guid DeviceId,
  ScriptResultStatus Status,
  int? ExitCode,
  string StandardOutput,
  string StandardError,
  bool OutputTruncated,
  string? FailureReason,
  DateTimeOffset? StartedAt,
  DateTimeOffset? FinishedAt);

public record ScriptRunDto(
  Guid Id,
  ScriptShell Shell,
  string Content,
  int TimeoutSeconds,
  string InitiatedBy,
  DateTimeOffset CreatedAt,
  ScriptResultDto[] Results);
