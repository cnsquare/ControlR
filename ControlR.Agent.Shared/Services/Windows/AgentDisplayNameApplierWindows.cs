using System.Diagnostics;
using System.Runtime.Versioning;
using System.ServiceProcess;
using ControlR.Agent.Shared.Options;
using ControlR.Libraries.Api.Contracts.Settings;
using ControlR.Libraries.Branding;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;
using Microsoft.Win32;

namespace ControlR.Agent.Shared.Services.Windows;

[SupportedOSPlatform("windows")]
internal class AgentDisplayNameApplierWindows(
  IOptions<InstanceOptions> instanceOptions,
  IFileSystemPathProvider pathProvider,
  ILogger<AgentDisplayNameApplierWindows> logger) : IAgentDisplayNameApplier
{
  private static readonly TimeSpan _commandTimeout = TimeSpan.FromSeconds(15);

  private readonly IOptions<InstanceOptions> _instanceOptions = instanceOptions;
  private readonly ILogger<AgentDisplayNameApplierWindows> _logger = logger;
  private readonly IFileSystemPathProvider _pathProvider = pathProvider;

  /// <summary>
  /// Each value is its own argument and no shell is involved, so a name is never parsed as a command.
  /// </summary>
  public static IReadOnlyList<string> BuildServiceConfigArguments(string serviceName, string displayName)
  {
    return ["config", serviceName, "DisplayName=", displayName];
  }

  public async Task Apply(string? displayName, CancellationToken cancellationToken)
  {
    // A copy run by hand could share an instance ID with a real install, and has no business
    // changing that install.
    if (!WindowsServiceHelpers.IsWindowsService())
    {
      return;
    }

    if (TenantSettingDefinitions.GetAgentDisplayNameError(displayName) is { } error)
    {
      _logger.LogWarning("Ignoring an agent name from the server. {Reason}", error);
      return;
    }

    var name = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
    var serviceName = GetServiceName();

    await ApplyToService(serviceName, name ?? serviceName, cancellationToken);
    ApplyToUninstallEntry(name ?? GetDefaultUninstallName());
  }

  private async Task ApplyToService(string serviceName, string displayName, CancellationToken cancellationToken)
  {
    using var service = ServiceController.GetServices().FirstOrDefault(x => x.ServiceName == serviceName);
    if (service is null || service.DisplayName == displayName)
    {
      return;
    }

    var startInfo = new ProcessStartInfo
    {
      FileName = Path.Combine(Environment.SystemDirectory, "sc.exe"),
      CreateNoWindow = true,
      RedirectStandardOutput = true,
      UseShellExecute = false,
    };

    foreach (var argument in BuildServiceConfigArguments(serviceName, displayName))
    {
      startInfo.ArgumentList.Add(argument);
    }

    using var process = Process.Start(startInfo)
      ?? throw new InvalidOperationException("Failed to start sc.exe.");

    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(_commandTimeout);

    var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
    await process.WaitForExitAsync(timeout.Token);

    if (process.ExitCode == 0)
    {
      _logger.LogInformation("Agent display name changed to {DisplayName}.", displayName);
    }
    else
    {
      _logger.LogWarning("Failed to change the agent display name. {Output}", output);
    }
  }

  private void ApplyToUninstallEntry(string displayName)
  {
    var view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;
    using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
    using var key = baseKey.OpenSubKey(_pathProvider.GetUninstallKeyPath(), writable: true);

    if (key is not null && !Equals(key.GetValue("DisplayName"), displayName))
    {
      key.SetValue("DisplayName", displayName);
    }
  }

  private string GetDefaultUninstallName()
  {
    return _instanceOptions.Value.InstanceId is { } instanceId
      ? $"{BrandingConstants.BrandName} Agent ({instanceId})"
      : $"{BrandingConstants.BrandName} Agent";
  }

  private string GetServiceName()
  {
    return string.IsNullOrWhiteSpace(_instanceOptions.Value.InstanceId)
      ? BrandingConstants.WindowsServiceBaseName
      : $"{BrandingConstants.WindowsServiceBaseName} ({_instanceOptions.Value.InstanceId})";
  }
}
