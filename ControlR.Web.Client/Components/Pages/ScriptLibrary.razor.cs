using System.Globalization;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1;
using Microsoft.AspNetCore.Components.Authorization;

namespace ControlR.Web.Client.Components.Pages;

// ReSharper disable once ClassNeverInstantiated.Global
public partial class ScriptLibrary
{
  private const int MaxDevicesListed = 500;

  private bool _canWrite;
  private DeviceResponseDto[] _devices = [];
  private Guid? _editingScriptId;
  private IReadOnlyCollection<Guid> _scheduleDeviceIds = [];
  private ScriptScheduleInterval _scheduleInterval = ScriptScheduleInterval.Daily;
  private string _scheduleName = string.Empty;
  private Guid? _scheduleScriptId;
  private ScriptScheduleDto[] _schedules = [];
  private string? _scheduleStart;
  private string _scriptContent = string.Empty;
  private string _scriptName = string.Empty;
  private SavedScriptDto[] _scripts = [];
  private ScriptShell _scriptShell = ScriptShell.WindowsPowerShell;
  private int _scriptTimeout = ScriptLimits.DefaultTimeoutSeconds;
  private Guid _tenantId;

  [Inject]
  public required AuthenticationStateProvider AuthState { get; init; }
  [Inject]
  public required IControlrApi ControlrApi { get; init; }
  [Inject]
  public required ISnackbar Snackbar { get; init; }

  private bool CanCreateSchedule =>
    !string.IsNullOrWhiteSpace(_scheduleName) && _scheduleScriptId is not null && _scheduleDeviceIds.Count > 0;

  private bool CanSaveScript =>
    !string.IsNullOrWhiteSpace(_scriptName) && !string.IsNullOrWhiteSpace(_scriptContent);

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
      startAt);

    var result = await ControlrApi.V1.ScriptSchedules.CreateScriptSchedule(_tenantId, request);
    if (!result.IsSuccess)
    {
      Snackbar.Add($"Failed to create the schedule: {result.Reason}", Severity.Error);
      return;
    }

    _scheduleName = string.Empty;
    _scheduleScriptId = null;
    _scheduleDeviceIds = [];
    _scheduleStart = null;
    Snackbar.Add("Schedule created", Severity.Success);
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
    _scriptName = script.Name;
    _scriptShell = script.Shell;
    _scriptTimeout = script.TimeoutSeconds;
  }

  private string GetDeviceName(Guid deviceId)
  {
    return _devices.FirstOrDefault(x => x.Id == deviceId)?.Name ?? deviceId.ToString();
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
    _scriptName = string.Empty;
    _scriptShell = ScriptShell.WindowsPowerShell;
    _scriptTimeout = ScriptLimits.DefaultTimeoutSeconds;
  }

  private async Task SaveScript()
  {
    var request = new SaveSavedScriptRequestDto(_scriptName, _scriptShell, _scriptContent, _scriptTimeout);

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
