using System.Net.Http.Json;
using ControlR.ApiClient.Interfaces.V1;
using ControlR.Libraries.Api.Contracts.Constants;
using ControlR.Libraries.Api.Contracts.Dtos;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1;

namespace ControlR.ApiClient;

internal partial class V1Api
{
  async Task<ApiResult> IDeviceAlertsApi.DismissDeviceAlert(Guid alertId, Guid tenantId, CancellationToken cancellationToken)
  {
    return await _client.ExecuteApiCall(async () =>
    {
      using var response = await _client.HttpClient.PostAsync(
        $"{HttpConstants.V1.DeviceAlertsEndpoint}/{alertId}/dismiss?tenantId={tenantId}", null, cancellationToken);
      await response.EnsureSuccessStatusCodeWithDetails();
    });
  }

  async Task<ApiResult<AlertSettingsDto>> IDeviceAlertsApi.GetAlertSettings(Guid tenantId, CancellationToken cancellationToken)
  {
    return await _client.ExecuteApiCall(async () =>
    {
      using var response = await _client.HttpClient.GetAsync(
        $"{HttpConstants.V1.DeviceAlertsEndpoint}/settings?tenantId={tenantId}", cancellationToken);
      await response.EnsureSuccessStatusCodeWithDetails();
      return await response.Content.ReadFromJsonAsync<AlertSettingsDto>(cancellationToken);
    });
  }

  async Task<ApiResult<DeviceAlertDto[]>> IDeviceAlertsApi.GetDeviceAlerts(Guid tenantId, CancellationToken cancellationToken)
  {
    return await _client.ExecuteApiCall(async () =>
    {
      using var response = await _client.HttpClient.GetAsync(
        $"{HttpConstants.V1.DeviceAlertsEndpoint}?tenantId={tenantId}", cancellationToken);
      await response.EnsureSuccessStatusCodeWithDetails();
      return await response.Content.ReadFromJsonAsync<DeviceAlertDto[]>(cancellationToken);
    });
  }

  async Task<ApiResult> IDeviceAlertsApi.TestAlertSettings(Guid tenantId, CancellationToken cancellationToken)
  {
    return await _client.ExecuteApiCall(async () =>
    {
      using var response = await _client.HttpClient.PostAsync(
        $"{HttpConstants.V1.DeviceAlertsEndpoint}/settings/test?tenantId={tenantId}", null, cancellationToken);
      await response.EnsureSuccessStatusCodeWithDetails();
    });
  }

  async Task<ApiResult<AlertSettingsDto>> IDeviceAlertsApi.UpdateAlertSettings(
    Guid tenantId,
    AlertSettingsDto request,
    CancellationToken cancellationToken)
  {
    return await _client.ExecuteApiCall(async () =>
    {
      using var response = await _client.HttpClient.PutAsJsonAsync(
        $"{HttpConstants.V1.DeviceAlertsEndpoint}/settings?tenantId={tenantId}", request, cancellationToken);
      await response.EnsureSuccessStatusCodeWithDetails();
      return await response.Content.ReadFromJsonAsync<AlertSettingsDto>(cancellationToken);
    });
  }
}
