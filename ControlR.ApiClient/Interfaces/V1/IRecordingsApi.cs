using ControlR.Libraries.Api.Contracts.Constants;
using ControlR.Libraries.Api.Contracts.Dtos;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1.Recordings;

namespace ControlR.ApiClient.Interfaces.V1;

public interface IRecordingsApi
{
  [ApiRoute($"{HttpConstants.V1.RecordingsEndpoint}", "GET")]
  Task<ApiResult<IReadOnlyList<RecordingResponseDto>>> GetRecordings(CancellationToken cancellationToken = default);

  [ApiRoute($"{HttpConstants.V1.RecordingsEndpoint}", "POST")]
  Task<ApiResult<RecordingResponseDto>> UploadRecording(
    Stream content,
    string contentType,
    Guid? deviceId = null,
    string? deviceName = null,
    int? durationSeconds = null,
    CancellationToken cancellationToken = default);

  [ApiRoute($"{HttpConstants.V1.RecordingsEndpoint}/{{id}}/content", "GET")]
  Task<ApiResult<byte[]>> GetRecordingContent(Guid id, CancellationToken cancellationToken = default);

  [ApiRoute($"{HttpConstants.V1.RecordingsEndpoint}/{{id}}", "DELETE")]
  Task<ApiResult> DeleteRecording(Guid id, CancellationToken cancellationToken = default);
}
