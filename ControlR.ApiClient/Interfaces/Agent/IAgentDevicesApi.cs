using ControlR.Libraries.Api.Contracts.Constants;
using ControlR.Libraries.Api.Contracts.Dtos;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.Internal;

namespace ControlR.ApiClient.Interfaces.Agent;

public interface IAgentDevicesApi
{
  [ApiRoute($"{HttpConstants.Agent.DevicesEndpoint}", "POST")]
  Task<ApiResult> CreateDevice(CreateDeviceRequestDto request, CancellationToken cancellationToken = default);

  [ApiRoute($"{HttpConstants.Agent.DeviceReenrollEndpoint}", "POST")]
  Task<ApiResult> ReenrollDevice(ReenrollDeviceRequestDto request, CancellationToken cancellationToken = default);
}
