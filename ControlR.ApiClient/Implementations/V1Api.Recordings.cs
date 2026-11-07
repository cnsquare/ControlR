using System.Net.Http.Json;
using ControlR.ApiClient.Interfaces.V1;
using ControlR.Libraries.Api.Contracts.Constants;
using ControlR.Libraries.Api.Contracts.Dtos;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1.Recordings;

namespace ControlR.ApiClient;

internal partial class V1Api
{
  async Task<ApiResult<IReadOnlyList<RecordingResponseDto>>> IRecordingsApi.GetRecordings(CancellationToken cancellationToken)
  {
    return await _client.ExecuteApiCall(async () =>
    {
      var result = await _client.HttpClient.GetFromJsonAsync<IReadOnlyList<RecordingResponseDto>>(
        HttpConstants.V1.RecordingsEndpoint, cancellationToken);
      return result ?? [];
    });
  }

  async Task<ApiResult> IRecordingsApi.DeleteRecording(Guid id, CancellationToken cancellationToken)
  {
    return await _client.ExecuteApiCall(async () =>
    {
      using var response = await _client.HttpClient.DeleteAsync(
        $"{HttpConstants.V1.RecordingsEndpoint}/{id}", cancellationToken);
      await response.EnsureSuccessStatusCodeWithDetails();
    });
  }
}
