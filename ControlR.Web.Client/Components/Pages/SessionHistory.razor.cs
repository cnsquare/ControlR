using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1;
using Microsoft.AspNetCore.Components.Authorization;

namespace ControlR.Web.Client.Components.Pages;

// ReSharper disable once ClassNeverInstantiated.Global
public partial class SessionHistory
{
  private string? _filter;
  private bool _isLoaded;
  private RemoteSessionLogDto[] _logs = [];

  [Inject]
  public required AuthenticationStateProvider AuthState { get; init; }
  [Inject]
  public required IControlrApi ControlrApi { get; init; }
  [Inject]
  public required ISnackbar Snackbar { get; init; }

  private RemoteSessionLogDto[] FilteredLogs =>
    string.IsNullOrWhiteSpace(_filter)
      ? _logs
      : [.. _logs.Where(x => Matches(x, _filter.Trim()))];

  protected override async Task OnInitializedAsync()
  {
    await base.OnInitializedAsync();

    var state = await AuthState.GetAuthenticationStateAsync();
    if (!state.User.TryGetTenantId(Snackbar, out var tenantId))
    {
      _isLoaded = true;
      return;
    }

    var result = await ControlrApi.V1.RemoteSessionLogs.GetRemoteSessionLogs(tenantId);
    if (result.IsSuccess && result.Value is { } logs)
    {
      _logs = logs;
    }
    else
    {
      Snackbar.Add("Failed to load the session history.", Severity.Error);
    }

    _isLoaded = true;
  }

  private static string GetDuration(RemoteSessionLogDto log)
  {
    if (log.EndedAt is not { } endedAt || log.FailureReason is not null)
    {
      return string.Empty;
    }

    return (endedAt - log.StartedAt).ToString(@"hh\:mm\:ss");
  }

  private static string GetStatus(RemoteSessionLogDto log)
  {
    if (log.FailureReason is { } reason)
    {
      return $"Failed: {reason}";
    }

    if (log.WasInterrupted)
    {
      return "End unknown (server restarted)";
    }

    return log.EndedAt is null ? "In progress" : "Ended";
  }

  private static bool Matches(RemoteSessionLogDto log, string filter)
  {
    return log.DeviceName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
      log.UserName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
      (log.IpAddress?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
  }
}
