using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1;

namespace ControlR.Web.Client.Components.Pages.DeviceAccess;

// ReSharper disable once ClassNeverInstantiated.Global
public partial class Scripts : IDisposable
{
  private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(2);

  // Past this the server's expiry job has already settled a result that never reported.
  private static readonly TimeSpan _pollGrace = TimeSpan.FromMinutes(6);

  private readonly CancellationTokenSource _disposeCts = new();

  private string _content = string.Empty;
  private bool _isRunning;
  private ScriptResultDto? _result;
  private ScriptShell _shell;
  private int _timeoutSeconds = ScriptLimits.DefaultTimeoutSeconds;

  [Inject]
  public required IControlrApi ControlrApi { get; init; }
  [SupplyParameterFromQuery]
  public required Guid DeviceId { get; init; }
  [Inject]
  public required IDeviceState DeviceState { get; init; }
  [Inject]
  public required ILogger<Scripts> Logger { get; init; }
  [Inject]
  public required ISnackbar Snackbar { get; init; }

  private IReadOnlyList<ScriptShell> AvailableShells =>
    ScriptShellOptions.GetAvailableShells(DeviceState.TryGetCurrentDevice()?.Platform ?? SystemPlatform.Unknown);

  private bool CanRun => !_isRunning && !string.IsNullOrWhiteSpace(_content);

  public void Dispose()
  {
    _disposeCts.Cancel();
    _disposeCts.Dispose();
    GC.SuppressFinalize(this);
  }

  protected override void OnInitialized()
  {
    base.OnInitialized();
    _shell = AvailableShells[0];
  }

  private async Task PollUntilSettled(Guid scriptRunId, Guid tenantId, CancellationToken cancellationToken)
  {
    var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(_timeoutSeconds) + _pollGrace;

    while (_result?.Status == ScriptResultStatus.Pending)
    {
      if (DateTimeOffset.UtcNow > deadline)
      {
        Snackbar.Add("Stopped waiting for the script result.", Severity.Warning);
        return;
      }

      await Task.Delay(_pollInterval, cancellationToken);

      var pollResult = await ControlrApi.V1.ScriptRuns.GetScriptRun(scriptRunId, tenantId, cancellationToken);
      if (!pollResult.IsSuccess)
      {
        Logger.LogError("Failed to read script run: {Error}", pollResult.Reason);
        Snackbar.Add($"Failed to read the script result: {pollResult.Reason}", Severity.Error);
        return;
      }

      _result = pollResult.Value.Results.FirstOrDefault(x => x.DeviceId == DeviceId);
      await InvokeAsync(StateHasChanged);
    }
  }

  private async Task RunScript()
  {
    if (!CanRun)
    {
      return;
    }

    _isRunning = true;
    _result = null;

    try
    {
      var tenantId = DeviceState.CurrentDevice.TenantId;
      var request = new CreateScriptRunRequestDto([DeviceId], _shell, _content, _timeoutSeconds);

      var createResult = await ControlrApi.V1.ScriptRuns.CreateScriptRun(tenantId, request, _disposeCts.Token);
      if (!createResult.IsSuccess)
      {
        Logger.LogError("Failed to start script: {Error}", createResult.Reason);
        Snackbar.Add($"Failed to start the script: {createResult.Reason}", Severity.Error);
        return;
      }

      _result = createResult.Value.Results.FirstOrDefault(x => x.DeviceId == DeviceId);
      await InvokeAsync(StateHasChanged);

      await PollUntilSettled(createResult.Value.Id, tenantId, _disposeCts.Token);
    }
    catch (OperationCanceledException)
    {
      // The user left the page. The script keeps running on the device.
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Error while running script.");
      Snackbar.Add("An error occurred while running the script.", Severity.Error);
    }
    finally
    {
      _isRunning = false;
      if (!_disposeCts.IsCancellationRequested)
      {
        await InvokeAsync(StateHasChanged);
      }
    }
  }
}
