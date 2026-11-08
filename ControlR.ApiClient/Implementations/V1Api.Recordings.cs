using System.Net.Http.Headers;
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

  async Task<ApiResult<RecordingResponseDto>> IRecordingsApi.UploadRecording(
    Stream content,
    string contentType,
    Guid? deviceId,
    string? deviceName,
    int? durationSeconds,
    CancellationToken cancellationToken)
  {
    return await _client.ExecuteApiCall(async () =>
    {
      using var form = new MultipartFormDataContent();
      var fileContent = new StreamContent(content);
      fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
      form.Add(fileContent, "file", "recording");
      if (deviceId.HasValue)
      {
        form.Add(new StringContent(deviceId.Value.ToString()), "deviceId");
      }
      if (deviceName is not null)
      {
        form.Add(new StringContent(deviceName), "deviceName");
      }
      if (durationSeconds.HasValue)
      {
        form.Add(new StringContent(durationSeconds.Value.ToString()), "durationSeconds");
      }

      using var response = await _client.HttpClient.PostAsync(
        HttpConstants.V1.RecordingsEndpoint, form, cancellationToken);
      await response.EnsureSuccessStatusCodeWithDetails();
      var dto = await response.Content.ReadFromJsonAsync<RecordingResponseDto>(cancellationToken);
      return dto ?? throw new InvalidOperationException("Upload returned no recording.");
    });
  }

  async Task<ApiResult<byte[]>> IRecordingsApi.GetRecordingContent(Guid id, CancellationToken cancellationToken)
  {
    return await _client.ExecuteApiCall(async () =>
    {
      return await _client.HttpClient.GetByteArrayAsync(
        $"{HttpConstants.V1.RecordingsEndpoint}/{id}/content", cancellationToken);
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
