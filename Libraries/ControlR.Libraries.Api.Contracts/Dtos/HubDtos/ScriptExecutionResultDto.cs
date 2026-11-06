namespace ControlR.Libraries.Api.Contracts.Dtos.HubDtos;

[MessagePackObject(keyAsPropertyName: true)]
public record ScriptExecutionResultDto(
  Guid ScriptResultId,
  int? ExitCode,
  string StandardOutput,
  string StandardError,
  bool OutputTruncated,
  bool TimedOut,
  string? FailureReason,
  DateTimeOffset StartedAt,
  DateTimeOffset FinishedAt);
