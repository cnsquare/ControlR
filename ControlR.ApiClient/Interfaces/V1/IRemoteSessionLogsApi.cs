using ControlR.Libraries.Api.Contracts.Constants;
using ControlR.Libraries.Api.Contracts.Dtos;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1;

namespace ControlR.ApiClient.Interfaces.V1;

public interface IRemoteSessionLogsApi
{
  [ApiRoute($"{HttpConstants.V1.RemoteSessionLogsEndpoint}?tenantId={{tenantId}}", "GET")]
  Task<ApiResult<RemoteSessionLogDto[]>> GetRemoteSessionLogs(Guid tenantId, CancellationToken cancellationToken = default);
}
