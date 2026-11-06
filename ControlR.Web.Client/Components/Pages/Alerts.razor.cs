using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1;
using Microsoft.AspNetCore.Components.Authorization;

namespace ControlR.Web.Client.Components.Pages;

// ReSharper disable once ClassNeverInstantiated.Global
public partial class Alerts
{
  private DeviceAlertDto[] _alerts = [];
  private bool _canWrite;
  private string? _email;
  private int _lowDiskPercent;
  private int _offlineMinutes;
  private Guid _tenantId;
  private string? _webhookUrl;

  [Inject]
  public required AuthenticationStateProvider AuthState { get; init; }
  [Inject]
  public required IControlrApi ControlrApi { get; init; }
  [Inject]
  public required ISnackbar Snackbar { get; init; }

  protected override async Task OnInitializedAsync()
  {
    await base.OnInitializedAsync();

    var state = await AuthState.GetAuthenticationStateAsync();
    _canWrite = state.User.HasClientPolicy(PolicyNames.RequireTenantSettingsWrite);

    if (!state.User.TryGetTenantId(Snackbar, out var tenantId))
    {
      return;
    }

    _tenantId = tenantId;
    await LoadAlerts();

    if (!_canWrite)
    {
      return;
    }

    var settings = await ControlrApi.V1.DeviceAlerts.GetAlertSettings(_tenantId);
    if (settings.IsSuccess && settings.Value is { } value)
    {
      _email = value.Email;
      _lowDiskPercent = value.LowDiskPercent;
      _offlineMinutes = value.OfflineMinutes;
      _webhookUrl = value.WebhookUrl;
    }
    else
    {
      Snackbar.Add("Failed to load the alert settings.", Severity.Error);
    }
  }

  private async Task Dismiss(DeviceAlertDto alert)
  {
    var result = await ControlrApi.V1.DeviceAlerts.DismissDeviceAlert(alert.Id, _tenantId);
    if (!result.IsSuccess)
    {
      Snackbar.Add("Failed to dismiss the alert.", Severity.Error);
      return;
    }

    await LoadAlerts();
  }

  private async Task LoadAlerts()
  {
    var result = await ControlrApi.V1.DeviceAlerts.GetDeviceAlerts(_tenantId);
    if (result.IsSuccess && result.Value is { } alerts)
    {
      _alerts = alerts;
    }
    else
    {
      Snackbar.Add("Failed to load the alerts.", Severity.Error);
    }
  }

  private async Task Save()
  {
    var result = await ControlrApi.V1.DeviceAlerts.UpdateAlertSettings(
      _tenantId,
      new AlertSettingsDto(_offlineMinutes, _lowDiskPercent, _email, _webhookUrl));

    if (result.IsSuccess)
    {
      Snackbar.Add("Alert settings saved.", Severity.Success);
    }
    else
    {
      Snackbar.Add(result.Reason ?? "Failed to save the alert settings.", Severity.Error);
    }
  }

  private async Task SendTest()
  {
    var result = await ControlrApi.V1.DeviceAlerts.TestAlertSettings(_tenantId);
    if (result.IsSuccess)
    {
      Snackbar.Add("Test sent.", Severity.Success);
    }
    else
    {
      Snackbar.Add(result.Reason ?? "The test could not be delivered.", Severity.Error);
    }
  }
}
