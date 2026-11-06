using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;

namespace ControlR.Agent.Common.Services.Scripting;

internal interface IScriptRunService
{
  bool TryStart(RunScriptRequestDto request);
}

internal class ScriptRunService(
  IHubConnection<IAgentHub> hubConnection,
  IScriptExecutor scriptExecutor,
  IRetryer retryer,
  IHostApplicationLifetime appLifetime,
  ILogger<ScriptRunService> logger) : IScriptRunService
{
  private const int MaxConcurrentRuns = 4;
  private const int ReportTryCount = 5;

  private static readonly TimeSpan _reportRetryDelay = TimeSpan.FromSeconds(10);

  private readonly IHostApplicationLifetime _appLifetime = appLifetime;
  private readonly IHubConnection<IAgentHub> _hubConnection = hubConnection;
  private readonly ILogger<ScriptRunService> _logger = logger;
  private readonly IRetryer _retryer = retryer;
  private readonly ConcurrentDictionary<Guid, byte> _running = new();
  private readonly IScriptExecutor _scriptExecutor = scriptExecutor;

  public bool TryStart(RunScriptRequestDto request)
  {
    if (_running.Count >= MaxConcurrentRuns || !_running.TryAdd(request.ScriptResultId, 0))
    {
      return false;
    }

    _ = Task.Run(() => RunAndReport(request));
    return true;
  }

  private async Task RunAndReport(RunScriptRequestDto request)
  {
    try
    {
      var result = await _scriptExecutor.Execute(request, _appLifetime.ApplicationStopping);

      await _retryer.Retry(
        async () =>
        {
          var reportResult = await _hubConnection.Server.ReportScriptResult(result);
          if (!reportResult.IsSuccess)
          {
            throw new InvalidOperationException(reportResult.Reason);
          }
        },
        ReportTryCount,
        _reportRetryDelay);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to report script result {ScriptResultId}.", request.ScriptResultId);
    }
    finally
    {
      _running.TryRemove(request.ScriptResultId, out _);
    }
  }
}
