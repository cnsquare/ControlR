using ControlR.Libraries.Api.Contracts.Constants;
using ControlR.Libraries.Api.Contracts.Dtos;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1;

namespace ControlR.ApiClient.Interfaces.V1;

public interface IDeviceAlertsApi
{
  [ApiRoute($"{HttpConstants.V1.DeviceAlertsEndpoint}/{{alertId}}/dismiss?tenantId={{tenantId}}", "POST")]
  Task<ApiResult> DismissDeviceAlert(Guid alertId, Guid tenantId, CancellationToken cancellationToken = default);

  [ApiRoute($"{HttpConstants.V1.DeviceAlertsEndpoint}/settings?tenantId={{tenantId}}", "GET")]
  Task<ApiResult<AlertSettingsDto>> GetAlertSettings(Guid tenantId, CancellationToken cancellationToken = default);

  [ApiRoute($"{HttpConstants.V1.DeviceAlertsEndpoint}?tenantId={{tenantId}}", "GET")]
  Task<ApiResult<DeviceAlertDto[]>> GetDeviceAlerts(Guid tenantId, CancellationToken cancellationToken = default);

  [ApiRoute($"{HttpConstants.V1.DeviceAlertsEndpoint}/settings/test?tenantId={{tenantId}}", "POST")]
  Task<ApiResult> TestAlertSettings(Guid tenantId, CancellationToken cancellationToken = default);

  [ApiRoute($"{HttpConstants.V1.DeviceAlertsEndpoint}/settings?tenantId={{tenantId}}", "PUT")]
  Task<ApiResult<AlertSettingsDto>> UpdateAlertSettings(
    Guid tenantId,
    AlertSettingsDto request,
    CancellationToken cancellationToken = default);
}
