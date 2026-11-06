using System.Globalization;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace ControlR.Web.Client.Components.Pages;

// ReSharper disable once ClassNeverInstantiated.Global
public partial class ScriptLibrary : IDisposable
{
  private const int MaxDevicesListed = 500;

  private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(2);
  private static readonly TimeSpan _runWatchLimit = TimeSpan.FromMinutes(5);

  private readonly CancellationTokenSource _disposeCts = new();

  private ScriptAlertDto[] _alerts = [];
  private bool _canWrite;
  private DeviceResponseDto[] _devices = [];
  private Guid? _editingScriptId;
  private bool _isRunning;
  private string _runContent = string.Empty;
  private IReadOnlyCollection<Guid> _runDeviceIds = [];
  private bool _runOnNextConnect;
  private ScriptResultDto[] _runResults = [];
  private Guid? _runScriptId;
  private ScriptShell _runShell = ScriptShell.WindowsPowerShell;
  private int _runTimeout = ScriptLimits.DefaultTimeoutSeconds;
  private string? _scheduleAlertEmail;
  private IReadOnlyCollection<Guid> _scheduleDeviceIds = [];
  private ScriptScheduleInterval _scheduleInterval = ScriptScheduleInterval.Daily;
  private string _scheduleName = string.Empty;
  private bool _scheduleRunOnNextConnect;
  private Guid? _scheduleScriptId;
  private ScriptScheduleDto[] _schedules = [];
  private string? _scheduleStart;
  private string _scriptContent = string.Empty;
  private string? _scriptFolder;
  private string _scriptName = string.Empty;
  private SavedScriptDto[] _scripts = [];
  private ScriptShell _scriptShell = ScriptShell.WindowsPowerShell;
  private int _scriptTimeout = ScriptLimits.DefaultTimeoutSeconds;
  private Guid _tenantId;

  [Inject]
  public required AuthenticationStateProvider AuthState { get; init; }
  [Inject]
  public required IControlrApi ControlrApi { get; init; }
  [SupplyParameterFromQuery(Name = "devices")]
  public string? DevicesQuery { get; init; }
  [Inject]
  public required IJSRuntime JsRuntime { get; init; }
  [Inject]
  public required ISnackbar Snackbar { get; init; }

  private bool CanRunNow =>
    !_isRunning && !string.IsNullOrWhiteSpace(_runContent) && _runDeviceIds.Count > 0;

  private bool CanCreateSchedule =>
    !string.IsNullOrWhiteSpace(_scheduleName) && _scheduleScriptId is not null && _scheduleDeviceIds.Count > 0;

  private bool CanSaveScript =>
    !string.IsNullOrWhiteSpace(_scriptName) && !string.IsNullOrWhiteSpace(_scriptContent);

  public void Dispose()
  {
    _disposeCts.Cancel();
    _disposeCts.Dispose();
    GC.SuppressFinalize(this);
  }

  protected override async Task OnInitializedAsync()
  {
    await base.OnInitializedAsync();

    var state = await AuthState.GetAuthenticationStateAsync();
    _canWrite = state.User.HasClientPolicy(PolicyNames.RequireScriptsWrite);

    if (!state.User.TryGetTenantId(Snackbar, out var tenantId))
    {
      return;
    }

    _tenantId = tenantId;
    await Refresh();
    await LoadDevices();

    // Sent by the dashboard's bulk actions. The server still checks each device when this runs.
    if (DevicesQuery is { Length: > 0 } devicesQuery)
    {
      _runDeviceIds =
      [
        .. devicesQuery
          .Split(',')
          .Select(x => Guid.TryParse(x, out var id) ? id : Guid.Empty)
          .Where(x => _devices.Any(device => device.Id == x))
      ];
    }
  }

  private async Task CreateSchedule()
  {
    if (_scheduleScriptId is not { } scriptId)
    {
      return;
    }

    DateTimeOffset? startAt = DateTime.TryParse(_scheduleStart, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var parsed)
      ? new DateTimeOffset(parsed)
      : null;

    var request = new CreateScriptScheduleRequestDto(
      _scheduleName,
      scriptId,
      [.. _scheduleDeviceIds],
      _scheduleInterval,
      startAt,
      _scheduleRunOnNextConnect,
      string.IsNullOrWhiteSpace(_scheduleAlertEmail) ? null : _scheduleAlertEmail.Trim());

    var result = await ControlrApi.V1.ScriptSchedules.CreateScriptSchedule(_tenantId, request);
    if (!result.IsSuccess)
    {
      Snackbar.Add($"Failed to create the schedule: {result.Reason}", Severity.Error);
      return;
    }

    _scheduleAlertEmail = null;
    _scheduleName = string.Empty;
    _scheduleRunOnNextConnect = false;
    _scheduleScriptId = null;
    _scheduleDeviceIds = [];
    _scheduleStart = null;
    Snackbar.Add("Schedule created", Severity.Success);
    await Refresh();
  }

  private async Task DismissAlert(ScriptAlertDto alert)
  {
    var result = await ControlrApi.V1.ScriptAlerts.DismissScriptAlert(alert.Id, _tenantId);
    if (!result.IsSuccess)
    {
      Snackbar.Add($"Failed to dismiss the alert: {result.Reason}", Severity.Error);
    }

    await Refresh();
  }

  private async Task DeleteSchedule(ScriptScheduleDto schedule)
  {
    var result = await ControlrApi.V1.ScriptSchedules.DeleteScriptSchedule(schedule.Id, _tenantId);
    if (!result.IsSuccess)
    {
      Snackbar.Add($"Failed to delete the schedule: {result.Reason}", Severity.Error);
      return;
    }

    await Refresh();
  }

  private async Task DeleteScript(SavedScriptDto script)
  {
    var result = await ControlrApi.V1.SavedScripts.DeleteSavedScript(script.Id, _tenantId);
    if (!result.IsSuccess)
    {
      Snackbar.Add($"Failed to delete the script: {result.Reason}", Severity.Error);
      return;
    }

    if (_editingScriptId == script.Id)
    {
      ResetScriptForm();
    }

    await Refresh();
  }

  private void EditScript(SavedScriptDto script)
  {
    _editingScriptId = script.Id;
    _scriptContent = script.Content;
    _scriptFolder = script.Folder;
    _scriptName = script.Name;
    _scriptShell = script.Shell;
    _scriptTimeout = script.TimeoutSeconds;
  }

  private string GetDeviceName(Guid deviceId)
  {
    return _devices.FirstOrDefault(x => x.Id == deviceId)?.Name ?? deviceId.ToString();
  }

  private void LoadRunScript(Guid? scriptId)
  {
    _runScriptId = scriptId;
    if (_scripts.FirstOrDefault(x => x.Id == scriptId) is { } script)
    {
      _runContent = script.Content;
      _runShell = script.Shell;
      _runTimeout = script.TimeoutSeconds;
    }
  }

  private async Task RunNow()
  {
    _isRunning = true;
    _runResults = [];

    try
    {
      var request = new CreateScriptRunRequestDto(
        [.. _runDeviceIds],
        _runShell,
        _runContent,
        _runTimeout,
        _runOnNextConnect);

      var created = await ControlrApi.V1.ScriptRuns.CreateScriptRun(_tenantId, request, _disposeCts.Token);
      if (!created.IsSuccess)
      {
        Snackbar.Add($"Failed to start the script: {created.Reason}", Severity.Error);
        return;
      }

      _runResults = created.Value.Results;
      await InvokeAsync(StateHasChanged);

      var deadline = DateTimeOffset.UtcNow + _runWatchLimit;
      while (_runResults.Any(x => x.Status == ScriptResultStatus.Pending) && DateTimeOffset.UtcNow < deadline)
      {
        await Task.Delay(_pollInterval, _disposeCts.Token);

        var run = await ControlrApi.V1.ScriptRuns.GetScriptRun(created.Value.Id, _tenantId, _disposeCts.Token);
        if (!run.IsSuccess)
        {
          Snackbar.Add($"Failed to read the results: {run.Reason}", Severity.Error);
          return;
        }

        _runResults = run.Value.Results;
        await InvokeAsync(StateHasChanged);
      }
    }
    catch (OperationCanceledException)
    {
      // The user left the page. The scripts keep running on the devices.
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

  private async Task ExportRunResults()
  {
    var csv = CsvBuilder.Build(
      ["Device", "Status", "Exit code", "Started", "Finished", "Output", "Errors", "Failure"],
      _runResults.Select(x => new[]
      {
        GetDeviceName(x.DeviceId),
        x.Status.ToString(),
        x.ExitCode?.ToString(CultureInfo.InvariantCulture),
        x.StartedAt?.ToString("u"),
        x.FinishedAt?.ToString("u"),
        x.StandardOutput,
        x.StandardError,
        x.FailureReason,
      }));

    await JsRuntime.InvokeVoidAsync("downloadCsvFile", "script-results.csv", csv);
  }

  private async Task LoadDevices()
  {
    var request = new DeviceSearchRequestDto { Page = 0, PageSize = MaxDevicesListed };
    var result = await ControlrApi.V1.Devices.SearchDevices(request);
    if (result.IsSuccess)
    {
      _devices = [.. result.Value.Items ?? []];
    }
  }

  private async Task Refresh()
  {
    var alerts = await ControlrApi.V1.ScriptAlerts.GetScriptAlerts(_tenantId);
    if (alerts.IsSuccess)
    {
      _alerts = alerts.Value;
    }

    var scripts = await ControlrApi.V1.SavedScripts.GetSavedScripts(_tenantId);
    if (scripts.IsSuccess)
    {
      _scripts = scripts.Value;
    }
    else
    {
      Snackbar.Add($"Failed to load saved scripts: {scripts.Reason}", Severity.Error);
    }

    var schedules = await ControlrApi.V1.ScriptSchedules.GetScriptSchedules(_tenantId);
    if (schedules.IsSuccess)
    {
      _schedules = schedules.Value;
    }
    else
    {
      Snackbar.Add($"Failed to load schedules: {schedules.Reason}", Severity.Error);
    }
  }

  private void ResetScriptForm()
  {
    _editingScriptId = null;
    _scriptContent = string.Empty;
    _scriptFolder = null;
    _scriptName = string.Empty;
    _scriptShell = ScriptShell.WindowsPowerShell;
    _scriptTimeout = ScriptLimits.DefaultTimeoutSeconds;
  }

  private async Task SaveScript()
  {
    var request = new SaveSavedScriptRequestDto(_scriptName, _scriptShell, _scriptContent, _scriptTimeout, _scriptFolder);

    var result = _editingScriptId is { } scriptId
      ? await ControlrApi.V1.SavedScripts.UpdateSavedScript(scriptId, _tenantId, request)
      : await ControlrApi.V1.SavedScripts.CreateSavedScript(_tenantId, request);

    if (!result.IsSuccess)
    {
      Snackbar.Add($"Failed to save the script: {result.Reason}", Severity.Error);
      return;
    }

    ResetScriptForm();
    Snackbar.Add("Script saved", Severity.Success);
    await Refresh();
  }

  private async Task SetScheduleEnabled(ScriptScheduleDto schedule, bool isEnabled)
  {
    var result = await ControlrApi.V1.ScriptSchedules.SetScriptScheduleEnabled(
      schedule.Id,
      _tenantId,
      new SetScriptScheduleEnabledRequestDto(isEnabled));

    if (!result.IsSuccess)
    {
      Snackbar.Add($"Failed to change the schedule: {result.Reason}", Severity.Error);
    }

    await Refresh();
  }
}
