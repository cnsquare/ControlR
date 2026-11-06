using ControlR.Libraries.Api.Contracts.Constants;
using ControlR.Libraries.Api.Contracts.Dtos;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1;

namespace ControlR.ApiClient.Interfaces.V1;

public interface IScriptSchedulesApi
{
  [ApiRoute($"{HttpConstants.V1.ScriptSchedulesEndpoint}?tenantId={{tenantId}}", "POST")]
  Task<ApiResult<ScriptScheduleDto>> CreateScriptSchedule(
    Guid tenantId,
    CreateScriptScheduleRequestDto request,
    CancellationToken cancellationToken = default);

  [ApiRoute($"{HttpConstants.V1.ScriptSchedulesEndpoint}/{{scheduleId}}?tenantId={{tenantId}}", "DELETE")]
  Task<ApiResult> DeleteScriptSchedule(Guid scheduleId, Guid tenantId, CancellationToken cancellationToken = default);

  [ApiRoute($"{HttpConstants.V1.ScriptSchedulesEndpoint}?tenantId={{tenantId}}", "GET")]
  Task<ApiResult<ScriptScheduleDto[]>> GetScriptSchedules(Guid tenantId, CancellationToken cancellationToken = default);

  [ApiRoute($"{HttpConstants.V1.ScriptSchedulesEndpoint}/{{scheduleId}}/enabled?tenantId={{tenantId}}", "PUT")]
  Task<ApiResult<ScriptScheduleDto>> SetScriptScheduleEnabled(
    Guid scheduleId,
    Guid tenantId,
    SetScriptScheduleEnabledRequestDto request,
    CancellationToken cancellationToken = default);
}
