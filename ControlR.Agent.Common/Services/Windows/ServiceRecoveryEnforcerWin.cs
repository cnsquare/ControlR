using System.Runtime.Versioning;
using ControlR.Libraries.Branding;
using ControlR.Libraries.Shared.Services.Processes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;

namespace ControlR.Agent.Common.Services.Windows;

/// <summary>
/// Re-applies the Windows service crash-recovery settings each time the agent starts, so the OS
/// keeps restarting the service after a failure even if those settings were cleared or never took.
/// This is the agent's own safety net: if the process dies, Windows brings it back.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ServiceRecoveryEnforcerWin(
  IProcessManager processManager,
  IOptions<InstanceOptions> instanceOptions,
  ILogger<ServiceRecoveryEnforcerWin> logger) : BackgroundService
{
  // Reset the failure count after a day of stability, then restart with growing backoff and never
  // give up. The install sets a single restart; this keeps the service recovering on repeat crashes.
  private const string FailureActions = "reset= 86400 actions= restart/5000/restart/10000/restart/30000";

  private readonly IOptions<InstanceOptions> _instanceOptions = instanceOptions;
  private readonly ILogger<ServiceRecoveryEnforcerWin> _logger = logger;
  private readonly IProcessManager _processManager = processManager;

  public static string BuildFailureArguments(string serviceName)
  {
    // sc.exe parses the quoted name as one token; the trailing switches follow with their spacing.
    return $"failure \"{serviceName}\" {FailureActions}";
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    // Only a real installed service has recovery settings to maintain. A console run has none.
    if (!WindowsServiceHelpers.IsWindowsService())
    {
      return;
    }

    try
    {
      var scPath = Path.Combine(Environment.SystemDirectory, "sc.exe");
      var result = await _processManager.GetProcessOutput(scPath, BuildFailureArguments(GetServiceName()));

      if (result.IsSuccess)
      {
        _logger.LogInformation("Service crash-recovery settings re-applied.");
      }
      else
      {
        _logger.LogWarning("Could not re-apply service crash-recovery settings. {Reason}", result.Reason);
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error while re-applying service crash-recovery settings.");
    }
  }

  private string GetServiceName()
  {
    return string.IsNullOrWhiteSpace(_instanceOptions.Value.InstanceId)
      ? BrandingConstants.WindowsServiceBaseName
      : $"{BrandingConstants.WindowsServiceBaseName} ({_instanceOptions.Value.InstanceId})";
  }
}
