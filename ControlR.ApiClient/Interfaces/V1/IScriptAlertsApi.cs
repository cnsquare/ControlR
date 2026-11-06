using ControlR.Libraries.Api.Contracts.Constants;
using ControlR.Libraries.Api.Contracts.Dtos;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1;

namespace ControlR.ApiClient.Interfaces.V1;

public interface IScriptAlertsApi
{
  [ApiRoute($"{HttpConstants.V1.ScriptAlertsEndpoint}/{{alertId}}/dismiss?tenantId={{tenantId}}", "POST")]
  Task<ApiResult> DismissScriptAlert(Guid alertId, Guid tenantId, CancellationToken cancellationToken = default);

  [ApiRoute($"{HttpConstants.V1.ScriptAlertsEndpoint}?tenantId={{tenantId}}", "GET")]
  Task<ApiResult<ScriptAlertDto[]>> GetScriptAlerts(Guid tenantId, CancellationToken cancellationToken = default);
}
