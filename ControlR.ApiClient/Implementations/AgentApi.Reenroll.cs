using System.Net.Http.Json;
using ControlR.ApiClient.Interfaces.Agent;
using ControlR.Libraries.Api.Contracts.Constants;
using ControlR.Libraries.Api.Contracts.Dtos;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.Internal;

namespace ControlR.ApiClient;

internal partial class AgentApi
{
  async Task<ApiResult> IAgentDevicesApi.ReenrollDevice(ReenrollDeviceRequestDto request, CancellationToken cancellationToken)
  {
    return await _client.ExecuteApiCall(async () =>
    {
      using var response = await _client.HttpClient.PostAsJsonAsync(
        HttpConstants.Agent.DeviceReenrollEndpoint, request, cancellationToken);
      await response.EnsureSuccessStatusCodeWithDetails();
    });
  }
}
