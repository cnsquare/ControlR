using System.Collections.Concurrent;
using ControlR.Libraries.Api.Contracts.Dtos.HubDtos;
using ControlR.Libraries.Api.Contracts.Hubs.Clients;
using Microsoft.AspNetCore.SignalR;

namespace ControlR.Web.Server.Services.Scripting;

public interface IScriptRunManager
{
  /// <summary>
  /// Records a run and sends it to the devices. The caller must already have
  /// authorized the principal against every device.
  /// </summary>
  Task<ScriptRun> Start(
    Guid tenantId,
    string initiatedBy,
    IReadOnlyList<Device> devices,
    ScriptShell shell,
    string content,
    int timeoutSeconds,
    CancellationToken cancellationToken);
}

public class ScriptRunManager(
  AppDb appDb,
  IHubContext<AgentHub, IAgentHubClient> agentHub,
  ILogger<ScriptRunManager> logger) : IScriptRunManager
{
  private const int MaxParallelDispatches = 32;

  private static readonly TimeSpan _agentAckTimeout = TimeSpan.FromSeconds(15);

  private readonly IHubContext<AgentHub, IAgentHubClient> _agentHub = agentHub;
  private readonly AppDb _appDb = appDb;
  private readonly ILogger<ScriptRunManager> _logger = logger;

  public async Task<ScriptRun> Start(
    Guid tenantId,
    string initiatedBy,
    IReadOnlyList<Device> devices,
    ScriptShell shell,
    string content,
    int timeoutSeconds,
    CancellationToken cancellationToken)
  {
    var run = new ScriptRun
    {
      Id = Guid.NewGuid(),
      Content = content,
      InitiatedBy = initiatedBy,
      Shell = shell,
      TenantId = tenantId,
      TimeoutSeconds = timeoutSeconds,
    };

    var results = devices
      .Select(device => new ScriptResult
      {
        Id = Guid.NewGuid(),
        DeviceId = device.Id,
        ScriptRunId = run.Id,
        TenantId = tenantId,
      })
      .ToList();

    run.Results = results;
    _appDb.ScriptRuns.Add(run);

    // Saved before dispatch, so a script that finishes instantly finds its row.
    await _appDb.SaveChangesAsync(cancellationToken);

    var connections = devices.ToDictionary(x => x.Id);
    var failures = new ConcurrentDictionary<Guid, string>();

    await Parallel.ForEachAsync(
      results,
      new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = MaxParallelDispatches },
      async (result, token) =>
      {
        var failure = await Dispatch(connections[result.DeviceId], result.Id, run, token);
        if (failure is not null)
        {
          failures[result.Id] = failure;
        }
      });

    foreach (var result in results.Where(x => failures.ContainsKey(x.Id)))
    {
      result.FailureReason = failures[result.Id];
      result.Status = ScriptResultStatus.Failed;
    }

    if (!failures.IsEmpty)
    {
      await _appDb.SaveChangesAsync(cancellationToken);
    }

    return run;
  }

  private async Task<string?> Dispatch(Device device, Guid resultId, ScriptRun run, CancellationToken cancellationToken)
  {
    // SignalR's Clients.Client("") is a no-op, so an empty connection id must not reach it.
    if (!device.IsOnline || string.IsNullOrWhiteSpace(device.ConnectionId))
    {
      return "The device is offline.";
    }

    try
    {
      var request = new RunScriptRequestDto(resultId, run.Shell, run.Content, run.TimeoutSeconds);
      var ack = await _agentHub.Clients
        .Client(device.ConnectionId)
        .RunScript(request)
        .WaitAsync(_agentAckTimeout, cancellationToken);

      return ack.IsSuccess ? null : ack.Reason ?? "The agent declined the script.";
    }
    catch (TimeoutException)
    {
      _logger.LogWarning("Device {DeviceId} did not acknowledge script result {ResultId}.", device.Id, resultId);
      return "The agent did not acknowledge the script.";
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      _logger.LogError(ex, "Failed to send script result {ResultId} to device {DeviceId}.", resultId, device.Id);
      return "The script could not be sent to the agent.";
    }
  }
}
