using System.Collections.Concurrent;
using ControlR.Libraries.Api.Contracts.Dtos.HubDtos;
using ControlR.Libraries.Api.Contracts.Hubs.Clients;
using ControlR.Web.Server.Authz.Permissions;
using ControlR.Web.Server.Services.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ControlR.Web.Server.Services.Scripting;

public sealed record ScriptRunRequest(
  Guid TenantId,
  string InitiatedBy,
  Guid? InitiatedByUserId,
  IReadOnlyList<Device> Devices,
  ScriptShell Shell,
  string Content,
  int TimeoutSeconds,
  bool RunOnNextConnect = false,
  Guid? ScriptScheduleId = null);

public interface IScriptRunManager
{
  /// <summary>
  /// Sends waiting results to devices that have come online. Time has passed since each was
  /// requested, so the requester's access is checked again first.
  /// </summary>
  Task<int> DispatchQueued(CancellationToken cancellationToken);

  /// <summary>
  /// Records a run and sends it to the devices. The caller must already have
  /// authorized the principal against every device.
  /// </summary>
  Task<ScriptRun> Start(ScriptRunRequest request, CancellationToken cancellationToken);
}

public class ScriptRunManager(
  AppDb appDb,
  IHubContext<AgentHub, IAgentHubClient> agentHub,
  IPermissionEvaluator permissionEvaluator,
  IResourceDescriptorFactory resourceFactory,
  TimeProvider timeProvider,
  ILogger<ScriptRunManager> logger) : IScriptRunManager
{
  public const string QueueExpiredReason = "The device did not connect in time, so the script was not run.";

  public static readonly TimeSpan QueueLifetime = TimeSpan.FromDays(7);

  private const int MaxDispatchesPerPass = 100;
  private const int MaxParallelDispatches = 32;

  private static readonly TimeSpan _agentAckTimeout = TimeSpan.FromSeconds(15);

  private readonly IHubContext<AgentHub, IAgentHubClient> _agentHub = agentHub;
  private readonly AppDb _appDb = appDb;
  private readonly ILogger<ScriptRunManager> _logger = logger;
  private readonly IPermissionEvaluator _permissionEvaluator = permissionEvaluator;
  private readonly IResourceDescriptorFactory _resourceFactory = resourceFactory;
  private readonly TimeProvider _timeProvider = timeProvider;

  public async Task<int> DispatchQueued(CancellationToken cancellationToken)
  {
    await ExpireQueued(cancellationToken);

    var ready = await _appDb.ScriptResults
      .Include(x => x.ScriptRun)
      .Include(x => x.Device)
      .Where(x => x.Status == ScriptResultStatus.Queued && x.Device!.IsOnline)
      .OrderBy(x => x.ScriptRun!.CreatedAt)
      .Take(MaxDispatchesPerPass)
      .ToListAsync(cancellationToken);

    var dispatched = 0;
    foreach (var result in ready)
    {
      if (await DispatchQueuedResult(result, cancellationToken))
      {
        dispatched++;
      }
    }

    return dispatched;
  }

  public async Task<ScriptRun> Start(ScriptRunRequest request, CancellationToken cancellationToken)
  {
    var now = _timeProvider.GetUtcNow();

    var run = new ScriptRun
    {
      Id = Guid.NewGuid(),
      Content = request.Content,
      CreatedAt = now,
      InitiatedBy = request.InitiatedBy,
      InitiatedByUserId = request.InitiatedByUserId,
      ScriptScheduleId = request.ScriptScheduleId,
      Shell = request.Shell,
      TenantId = request.TenantId,
      TimeoutSeconds = request.TimeoutSeconds,
    };

    var results = request.Devices
      .Select(device => new ScriptResult
      {
        Id = Guid.NewGuid(),
        DeviceId = device.Id,
        DispatchedAt = now,
        ScriptRunId = run.Id,
        TenantId = request.TenantId,
      })
      .ToList();

    run.Results = results;
    _appDb.ScriptRuns.Add(run);

    // Saved before dispatch, so a script that finishes instantly finds its row.
    await _appDb.SaveChangesAsync(cancellationToken);

    var devices = request.Devices.ToDictionary(x => x.Id);
    var outcomes = new ConcurrentDictionary<Guid, DispatchOutcome>();

    await Parallel.ForEachAsync(
      results,
      new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = MaxParallelDispatches },
      async (result, token) =>
      {
        outcomes[result.Id] = await Dispatch(devices[result.DeviceId], result.Id, run, token);
      });

    foreach (var result in results)
    {
      var outcome = outcomes[result.Id];
      if (!outcome.WasSent)
      {
        ApplyFailedDispatch(result, outcome, request.RunOnNextConnect);
      }
    }

    if (outcomes.Values.Any(x => !x.WasSent))
    {
      await _appDb.SaveChangesAsync(cancellationToken);
    }

    return run;
  }

  private static void ApplyFailedDispatch(ScriptResult result, DispatchOutcome outcome, bool mayQueue)
  {
    if (mayQueue && outcome.CanRetry)
    {
      result.DispatchedAt = null;
      result.FailureReason = null;
      result.Status = ScriptResultStatus.Queued;
      return;
    }

    result.FailureReason = outcome.Reason;
    result.Status = ScriptResultStatus.Failed;
  }

  private async Task<DispatchOutcome> Dispatch(Device device, Guid resultId, ScriptRun run, CancellationToken cancellationToken)
  {
    // SignalR's Clients.Client("") is a no-op, so an empty connection id must not reach it.
    if (!device.IsOnline || string.IsNullOrWhiteSpace(device.ConnectionId))
    {
      return DispatchOutcome.NotSent("The device is offline.", canRetry: true);
    }

    try
    {
      var request = new RunScriptRequestDto(resultId, run.Shell, run.Content, run.TimeoutSeconds);
      var ack = await _agentHub.Clients
        .Client(device.ConnectionId)
        .RunScript(request)
        .WaitAsync(_agentAckTimeout, cancellationToken);

      return ack.IsSuccess
        ? DispatchOutcome.Sent
        : DispatchOutcome.NotSent(ack.Reason ?? "The agent declined the script.", canRetry: true);
    }
    catch (TimeoutException)
    {
      // The agent may have started the script, so sending it again could run it twice.
      _logger.LogWarning("Device {DeviceId} did not acknowledge script result {ResultId}.", device.Id, resultId);
      return DispatchOutcome.NotSent("The agent did not acknowledge the script.", canRetry: false);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      _logger.LogError(ex, "Failed to send script result {ResultId} to device {DeviceId}.", resultId, device.Id);
      return DispatchOutcome.NotSent("The script could not be sent to the agent.", canRetry: true);
    }
  }

  private async Task<bool> DispatchQueuedResult(ScriptResult result, CancellationToken cancellationToken)
  {
    var run = result.ScriptRun!;
    var device = result.Device!;

    if (await GetRefusalReason(run, device, cancellationToken) is { } reason)
    {
      result.FailureReason = reason;
      result.Status = ScriptResultStatus.Failed;
      await _appDb.SaveChangesAsync(cancellationToken);
      return false;
    }

    // Saved before sending, so a crash here leaves it pending rather than sending it twice.
    result.DispatchedAt = _timeProvider.GetUtcNow();
    result.Status = ScriptResultStatus.Pending;
    await _appDb.SaveChangesAsync(cancellationToken);

    var outcome = await Dispatch(device, result.Id, run, cancellationToken);
    if (outcome.WasSent)
    {
      return true;
    }

    ApplyFailedDispatch(result, outcome, mayQueue: true);
    await _appDb.SaveChangesAsync(cancellationToken);
    return false;
  }

  private async Task ExpireQueued(CancellationToken cancellationToken)
  {
    var cutoff = _timeProvider.GetUtcNow() - QueueLifetime;

    var expired = await _appDb.ScriptResults
      .Where(x => x.Status == ScriptResultStatus.Queued && x.ScriptRun!.CreatedAt < cutoff)
      .ToListAsync(cancellationToken);

    if (expired.Count == 0)
    {
      return;
    }

    foreach (var result in expired)
    {
      result.FailureReason = QueueExpiredReason;
      result.Status = ScriptResultStatus.Failed;
    }

    await _appDb.SaveChangesAsync(cancellationToken);
  }

  private async Task<string?> GetRefusalReason(ScriptRun run, Device device, CancellationToken cancellationToken)
  {
    if (run.ScriptScheduleId is { } scheduleId)
    {
      var isScheduleActive = await _appDb.ScriptSchedules
        .AnyAsync(x => x.Id == scheduleId && x.TenantId == run.TenantId && x.IsEnabled, cancellationToken);

      if (!isScheduleActive)
      {
        return "Its schedule was paused or removed before the device connected.";
      }
    }

    // Matching the tenant matters. An invite can move a user to another tenant entirely.
    var user = await _appDb.Users
      .AsNoTracking()
      .FirstOrDefaultAsync(x => x.Id == run.InitiatedByUserId && x.TenantId == run.TenantId, cancellationToken);

    if (user is null)
    {
      return "The person who requested it is no longer in this tenant.";
    }

    var principal = new PrincipalDescriptor(
      PrincipalType.User,
      user.Id,
      user.TenantId,
      AuthMethod: "queued-script-run");

    var resource = await _resourceFactory.CreateDevice(device, cancellationToken);
    var decision = await _permissionEvaluator.Evaluate(
      principal,
      PermissionNames.DeviceScriptRun,
      resource,
      cancellationToken);

    return decision.Allowed
      ? null
      : "The person who requested it can no longer run scripts on this device.";
  }

  private sealed record DispatchOutcome(bool WasSent, bool CanRetry, string? Reason)
  {
    public static DispatchOutcome Sent { get; } = new(true, false, null);

    public static DispatchOutcome NotSent(string reason, bool canRetry) => new(false, canRetry, reason);
  }
}
