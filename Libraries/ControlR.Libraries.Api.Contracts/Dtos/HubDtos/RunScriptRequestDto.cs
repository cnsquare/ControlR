namespace ControlR.Libraries.Api.Contracts.Dtos.HubDtos;

[MessagePackObject(keyAsPropertyName: true)]
public record RunScriptRequestDto(
  Guid ScriptResultId,
  ScriptShell Shell,
  string Content,
  int TimeoutSeconds);
