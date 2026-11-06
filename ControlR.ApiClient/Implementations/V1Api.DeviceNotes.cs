using System.Net.Http.Json;
using ControlR.ApiClient.Interfaces.V1;
using ControlR.Libraries.Api.Contracts.Constants;
using ControlR.Libraries.Api.Contracts.Dtos;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1;

namespace ControlR.ApiClient;

internal partial class V1Api
{
  async Task<ApiResult<DeviceNotesDto>> IDeviceNotesApi.GetDeviceNotes(
    Guid deviceId,
    Guid tenantId,
    CancellationToken cancellationToken)
  {
    return await _client.ExecuteApiCall(async () =>
    {
      using var response = await _client.HttpClient.GetAsync(
        $"{HttpConstants.V1.DeviceNotesEndpoint}/{deviceId}?tenantId={tenantId}", cancellationToken);
      await response.EnsureSuccessStatusCodeWithDetails();
      return await response.Content.ReadFromJsonAsync<DeviceNotesDto>(cancellationToken);
    });
  }

  async Task<ApiResult<DeviceNotesDto>> IDeviceNotesApi.UpdateDeviceNotes(
    Guid deviceId,
    Guid tenantId,
    DeviceNotesDto request,
    CancellationToken cancellationToken)
  {
    return await _client.ExecuteApiCall(async () =>
    {
      using var response = await _client.HttpClient.PutAsJsonAsync(
        $"{HttpConstants.V1.DeviceNotesEndpoint}/{deviceId}?tenantId={tenantId}", request, cancellationToken);
      await response.EnsureSuccessStatusCodeWithDetails();
      return await response.Content.ReadFromJsonAsync<DeviceNotesDto>(cancellationToken);
    });
  }
}
