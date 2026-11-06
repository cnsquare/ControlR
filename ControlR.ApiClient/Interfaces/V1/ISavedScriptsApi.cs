using ControlR.Libraries.Api.Contracts.Constants;
using ControlR.Libraries.Api.Contracts.Dtos;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1;

namespace ControlR.ApiClient.Interfaces.V1;

public interface ISavedScriptsApi
{
  [ApiRoute($"{HttpConstants.V1.SavedScriptsEndpoint}?tenantId={{tenantId}}", "POST")]
  Task<ApiResult<SavedScriptDto>> CreateSavedScript(
    Guid tenantId,
    SaveSavedScriptRequestDto request,
    CancellationToken cancellationToken = default);

  [ApiRoute($"{HttpConstants.V1.SavedScriptsEndpoint}/{{savedScriptId}}?tenantId={{tenantId}}", "DELETE")]
  Task<ApiResult> DeleteSavedScript(Guid savedScriptId, Guid tenantId, CancellationToken cancellationToken = default);

  [ApiRoute($"{HttpConstants.V1.SavedScriptsEndpoint}?tenantId={{tenantId}}", "GET")]
  Task<ApiResult<SavedScriptDto[]>> GetSavedScripts(Guid tenantId, CancellationToken cancellationToken = default);

  [ApiRoute($"{HttpConstants.V1.SavedScriptsEndpoint}/{{savedScriptId}}?tenantId={{tenantId}}", "PUT")]
  Task<ApiResult<SavedScriptDto>> UpdateSavedScript(
    Guid savedScriptId,
    Guid tenantId,
    SaveSavedScriptRequestDto request,
    CancellationToken cancellationToken = default);
}
