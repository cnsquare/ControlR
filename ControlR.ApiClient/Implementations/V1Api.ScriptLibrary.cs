using System.Net.Http.Json;
using ControlR.ApiClient.Interfaces.V1;
using ControlR.Libraries.Api.Contracts.Constants;
using ControlR.Libraries.Api.Contracts.Dtos;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1;

namespace ControlR.ApiClient;

internal partial class V1Api
{
  async Task<ApiResult<SavedScriptDto>> ISavedScriptsApi.CreateSavedScript(
    Guid tenantId,
    SaveSavedScriptRequestDto request,
    CancellationToken cancellationToken)
  {
    return await _client.ExecuteApiCall(async () =>
    {
      using var response = await _client.HttpClient.PostAsJsonAsync(
        $"{HttpConstants.V1.SavedScriptsEndpoint}?tenantId={tenantId}", request, cancellationToken);
      await response.EnsureSuccessStatusCodeWithDetails();
      return await response.Content.ReadFromJsonAsync<SavedScriptDto>(cancellationToken);
    });
  }

  async Task<ApiResult<ScriptScheduleDto>> IScriptSchedulesApi.CreateScriptSchedule(
    Guid tenantId,
    CreateScriptScheduleRequestDto request,
    CancellationToken cancellationToken)
  {
    return await _client.ExecuteApiCall(async () =>
    {
      using var response = await _client.HttpClient.PostAsJsonAsync(
        $"{HttpConstants.V1.ScriptSchedulesEndpoint}?tenantId={tenantId}", request, cancellationToken);
      await response.EnsureSuccessStatusCodeWithDetails();
      return await response.Content.ReadFromJsonAsync<ScriptScheduleDto>(cancellationToken);
    });
  }

  async Task<ApiResult> ISavedScriptsApi.DeleteSavedScript(Guid savedScriptId, Guid tenantId, CancellationToken cancellationToken)
  {
    return await _client.ExecuteApiCall(async () =>
    {
      using var response = await _client.HttpClient.DeleteAsync(
        $"{HttpConstants.V1.SavedScriptsEndpoint}/{savedScriptId}?tenantId={tenantId}", cancellationToken);
      await response.EnsureSuccessStatusCodeWithDetails();
    });
  }

  async Task<ApiResult> IScriptSchedulesApi.DeleteScriptSchedule(Guid scheduleId, Guid tenantId, CancellationToken cancellationToken)
  {
    return await _client.ExecuteApiCall(async () =>
    {
      using var response = await _client.HttpClient.DeleteAsync(
        $"{HttpConstants.V1.ScriptSchedulesEndpoint}/{scheduleId}?tenantId={tenantId}", cancellationToken);
      await response.EnsureSuccessStatusCodeWithDetails();
    });
  }

  async Task<ApiResult<SavedScriptDto[]>> ISavedScriptsApi.GetSavedScripts(Guid tenantId, CancellationToken cancellationToken)
  {
    return await _client.ExecuteApiCall(async () =>
    {
      using var response = await _client.HttpClient.GetAsync(
        $"{HttpConstants.V1.SavedScriptsEndpoint}?tenantId={tenantId}", cancellationToken);
      await response.EnsureSuccessStatusCodeWithDetails();
      return await response.Content.ReadFromJsonAsync<SavedScriptDto[]>(cancellationToken);
    });
  }

  async Task<ApiResult<ScriptScheduleDto[]>> IScriptSchedulesApi.GetScriptSchedules(Guid tenantId, CancellationToken cancellationToken)
  {
    return await _client.ExecuteApiCall(async () =>
    {
      using var response = await _client.HttpClient.GetAsync(
        $"{HttpConstants.V1.ScriptSchedulesEndpoint}?tenantId={tenantId}", cancellationToken);
      await response.EnsureSuccessStatusCodeWithDetails();
      return await response.Content.ReadFromJsonAsync<ScriptScheduleDto[]>(cancellationToken);
    });
  }

  async Task<ApiResult<ScriptScheduleDto>> IScriptSchedulesApi.SetScriptScheduleEnabled(
    Guid scheduleId,
    Guid tenantId,
    SetScriptScheduleEnabledRequestDto request,
    CancellationToken cancellationToken)
  {
    return await _client.ExecuteApiCall(async () =>
    {
      using var response = await _client.HttpClient.PutAsJsonAsync(
        $"{HttpConstants.V1.ScriptSchedulesEndpoint}/{scheduleId}/enabled?tenantId={tenantId}", request, cancellationToken);
      await response.EnsureSuccessStatusCodeWithDetails();
      return await response.Content.ReadFromJsonAsync<ScriptScheduleDto>(cancellationToken);
    });
  }

  async Task<ApiResult<SavedScriptDto>> ISavedScriptsApi.UpdateSavedScript(
    Guid savedScriptId,
    Guid tenantId,
    SaveSavedScriptRequestDto request,
    CancellationToken cancellationToken)
  {
    return await _client.ExecuteApiCall(async () =>
    {
      using var response = await _client.HttpClient.PutAsJsonAsync(
        $"{HttpConstants.V1.SavedScriptsEndpoint}/{savedScriptId}?tenantId={tenantId}", request, cancellationToken);
      await response.EnsureSuccessStatusCodeWithDetails();
      return await response.Content.ReadFromJsonAsync<SavedScriptDto>(cancellationToken);
    });
  }
}
