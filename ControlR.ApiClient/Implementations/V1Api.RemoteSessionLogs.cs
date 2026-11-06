using System.Net.Http.Json;
using ControlR.ApiClient.Interfaces.V1;
using ControlR.Libraries.Api.Contracts.Constants;
using ControlR.Libraries.Api.Contracts.Dtos;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1;

namespace ControlR.ApiClient;

internal partial class V1Api
{
  async Task<ApiResult<RemoteSessionLogDto[]>> IRemoteSessionLogsApi.GetRemoteSessionLogs(
    Guid tenantId,
    CancellationToken cancellationToken)
  {
    return await _client.ExecuteApiCall(async () =>
    {
      using var response = await _client.HttpClient.GetAsync(
        $"{HttpConstants.V1.RemoteSessionLogsEndpoint}?tenantId={tenantId}", cancellationToken);
      await response.EnsureSuccessStatusCodeWithDetails();
      return await response.Content.ReadFromJsonAsync<RemoteSessionLogDto[]>(cancellationToken);
    });
  }
}
