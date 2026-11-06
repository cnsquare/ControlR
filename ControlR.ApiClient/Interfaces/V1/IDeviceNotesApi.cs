using ControlR.Libraries.Api.Contracts.Constants;
using ControlR.Libraries.Api.Contracts.Dtos;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1;

namespace ControlR.ApiClient.Interfaces.V1;

public interface IDeviceNotesApi
{
  [ApiRoute($"{HttpConstants.V1.DeviceNotesEndpoint}/{{deviceId}}?tenantId={{tenantId}}", "GET")]
  Task<ApiResult<DeviceNotesDto>> GetDeviceNotes(Guid deviceId, Guid tenantId, CancellationToken cancellationToken = default);

  [ApiRoute($"{HttpConstants.V1.DeviceNotesEndpoint}/{{deviceId}}?tenantId={{tenantId}}", "PUT")]
  Task<ApiResult<DeviceNotesDto>> UpdateDeviceNotes(
    Guid deviceId,
    Guid tenantId,
    DeviceNotesDto request,
    CancellationToken cancellationToken = default);
}
