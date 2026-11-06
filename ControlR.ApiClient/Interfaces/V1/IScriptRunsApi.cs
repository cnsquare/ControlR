using ControlR.Libraries.Api.Contracts.Constants;
using ControlR.Libraries.Api.Contracts.Dtos;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1;

namespace ControlR.ApiClient.Interfaces.V1;

public interface IScriptRunsApi
{
  /// <summary>
  /// Cancels the parts of a run still waiting for a device to connect.
  /// </summary>
  [ApiRoute($"{HttpConstants.V1.ScriptRunsEndpoint}/{{scriptRunId}}/cancel?tenantId={{tenantId}}", "POST")]
  Task<ApiResult> CancelQueuedScriptRun(
    Guid scriptRunId,
    Guid tenantId,
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Starts a script on every named device. The caller must hold the run permission on all of
  /// them, or nothing runs.
  /// </summary>
  [ApiRoute($"{HttpConstants.V1.ScriptRunsEndpoint}?tenantId={{tenantId}}", "POST")]
  Task<ApiResult<ScriptRunDto>> CreateScriptRun(
    Guid tenantId,
    CreateScriptRunRequestDto request,
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Lists a device's script runs, newest first. Output is left out, so read a run for its detail.
  /// </summary>
  [ApiRoute($"{HttpConstants.V1.ScriptRunsEndpoint}?deviceId={{deviceId}}&tenantId={{tenantId}}&limit={{limit}}", "GET")]
  Task<ApiResult<ScriptRunHistoryItemDto[]>> GetScriptRunHistory(
    Guid deviceId,
    Guid tenantId,
    int limit = ScriptLimits.HistoryDefaultCount,
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Reads a script run. Results are limited to the devices the caller may run scripts on.
  /// </summary>
  [ApiRoute($"{HttpConstants.V1.ScriptRunsEndpoint}/{{scriptRunId}}?tenantId={{tenantId}}", "GET")]
  Task<ApiResult<ScriptRunDto>> GetScriptRun(
    Guid scriptRunId,
    Guid tenantId,
    CancellationToken cancellationToken = default);
}
