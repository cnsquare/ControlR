using System.Net.Http.Json;
using ControlR.ApiClient.Interfaces.V1;
using ControlR.Libraries.Api.Contracts.Constants;
using ControlR.Libraries.Api.Contracts.Dtos;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1;

namespace ControlR.ApiClient;

internal partial class V1Api
{
  async Task<ApiResult> IScriptRunsApi.CancelQueuedScriptRun(
    Guid scriptRunId,
    Guid tenantId,
    CancellationToken cancellationToken)
  {
    return await _client.ExecuteApiCall(async () =>
    {
      using var response = await _client.HttpClient.PostAsync(
        $"{HttpConstants.V1.ScriptRunsEndpoint}/{scriptRunId}/cancel?tenantId={tenantId}", null, cancellationToken);
      await response.EnsureSuccessStatusCodeWithDetails();
    });
  }

  async Task<ApiResult<ScriptRunDto>> IScriptRunsApi.CreateScriptRun(
    Guid tenantId,
    CreateScriptRunRequestDto request,
    CancellationToken cancellationToken)
  {
    return await _client.ExecuteApiCall(async () =>
    {
      using var response = await _client.HttpClient.PostAsJsonAsync(
        $"{HttpConstants.V1.ScriptRunsEndpoint}?tenantId={tenantId}", request, cancellationToken);
      await response.EnsureSuccessStatusCodeWithDetails();
      return await response.Content.ReadFromJsonAsync<ScriptRunDto>(cancellationToken);
    });
  }

  async Task<ApiResult<ScriptRunHistoryItemDto[]>> IScriptRunsApi.GetScriptRunHistory(
    Guid deviceId,
    Guid tenantId,
    int limit,
    CancellationToken cancellationToken)
  {
    return await _client.ExecuteApiCall(async () =>
    {
      using var response = await _client.HttpClient.GetAsync(
        $"{HttpConstants.V1.ScriptRunsEndpoint}?deviceId={deviceId}&tenantId={tenantId}&limit={limit}", cancellationToken);
      await response.EnsureSuccessStatusCodeWithDetails();
      return await response.Content.ReadFromJsonAsync<ScriptRunHistoryItemDto[]>(cancellationToken);
    });
  }

  async Task<ApiResult<ScriptRunDto>> IScriptRunsApi.GetScriptRun(
    Guid scriptRunId,
    Guid tenantId,
    CancellationToken cancellationToken)
  {
    return await _client.ExecuteApiCall(async () =>
    {
      using var response = await _client.HttpClient.GetAsync(
        $"{HttpConstants.V1.ScriptRunsEndpoint}/{scriptRunId}?tenantId={tenantId}", cancellationToken);
      await response.EnsureSuccessStatusCodeWithDetails();
      return await response.Content.ReadFromJsonAsync<ScriptRunDto>(cancellationToken);
    });
  }
}
