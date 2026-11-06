using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace ControlR.Web.Client.Components.Pages;

// ReSharper disable once ClassNeverInstantiated.Global
public partial class SessionHistory
{
  private const int MaxLogs = 1000;

  private string? _filter;
  private bool _isLoaded;
  private RemoteSessionLogDto[] _logs = [];

  [Inject]
  public required AuthenticationStateProvider AuthState { get; init; }
  [Inject]
  public required IControlrApi ControlrApi { get; init; }
  [Inject]
  public required IJSRuntime JsRuntime { get; init; }
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

    var result = await ControlrApi.V1.RemoteSessionLogs.GetRemoteSessionLogs(tenantId, MaxLogs);
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

  private async Task ExportCsv()
  {
    var csv = CsvBuilder.Build(
      ["Started", "Ended", "Type", "Device", "User", "IP address", "Status", "Details"],
      FilteredLogs.Select(x => new[]
      {
        x.StartedAt.ToString("u"),
        x.EndedAt?.ToString("u"),
        GetKind(x),
        x.DeviceName,
        x.UserName,
        x.IpAddress,
        GetStatus(x),
        x.Detail,
      }));

    await JsRuntime.InvokeVoidAsync("downloadCsvFile", "session-history.csv", csv);
  }

  private static string GetDuration(RemoteSessionLogDto log)
  {
    if (log.EndedAt is not { } endedAt ||
        log.FailureReason is not null ||
        log.Kind == RemoteSessionKind.FileOperation)
    {
      return string.Empty;
    }

    return (endedAt - log.StartedAt).ToString(@"hh\:mm\:ss");
  }

  private static string GetKind(RemoteSessionLogDto log)
  {
    return log.Kind switch
    {
      RemoteSessionKind.RemoteControl => "Remote control",
      RemoteSessionKind.FlashAssist => "Flash Assist",
      RemoteSessionKind.Terminal => "Terminal",
      RemoteSessionKind.VncRelay => "VNC",
      RemoteSessionKind.FileOperation => "File",
      _ => log.Kind.ToString(),
    };
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

    if (log.Kind == RemoteSessionKind.FileOperation)
    {
      return "Requested";
    }

    return log.EndedAt is null ? "In progress" : "Ended";
  }

  private static bool Matches(RemoteSessionLogDto log, string filter)
  {
    return log.DeviceName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
      log.UserName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
      (log.Detail?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
      (log.IpAddress?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
  }
}
