using System.ComponentModel;
using System.Diagnostics;
using ControlR.Libraries.Api.Contracts.Dtos.HubDtos;
using ControlR.Libraries.Api.Contracts.Enums;
using ControlR.Libraries.Branding;
using ControlR.Libraries.Shared.Constants;
using ControlR.Libraries.Shared.Extensions;
using ControlR.Libraries.Shared.Primitives;
using ControlR.Libraries.Shared.Services.Processes;
using Microsoft.Extensions.Logging;

namespace ControlR.Assist.Services;

public interface IUnattendedAccessInstaller
{
  Task<Result> Install(Uri serverUri, UnattendedAccessGrantDto grant, CancellationToken cancellationToken);
}

/// <summary>
/// Installs the standard agent, which is what provides unattended access. It always goes
/// through the operating system's elevation prompt. That prompt runs on the secure desktop,
/// where a technician's remote input cannot reach it, so someone at the device must approve.
/// </summary>
public sealed class UnattendedAccessInstaller(
  IProcessManager processManager,
  ILogger<UnattendedAccessInstaller> logger) : IUnattendedAccessInstaller
{
  private const int ElevationCancelledError = 1223;

  private static readonly TimeSpan _installTimeout = TimeSpan.FromMinutes(10);

  private readonly ILogger<UnattendedAccessInstaller> _logger = logger;
  private readonly IProcessManager _processManager = processManager;

  public async Task<Result> Install(Uri serverUri, UnattendedAccessGrantDto grant, CancellationToken cancellationToken)
  {
    if (!OperatingSystem.IsWindows())
    {
      return Result.Fail(Localization.UnattendedAccessWindowsOnly);
    }

    string? directory = null;

    try
    {
      directory = Directory.CreateTempSubdirectory("controlr-install-").FullName;
      var installerPath = Path.Combine(directory, $"{BrandingConstants.InstallerBaseName}.exe");

      try
      {
        await Download(serverUri, installerPath, cancellationToken);
      }
      catch (Exception ex)
      {
        // Surface the real reason instead of a generic failure, so a technician can tell whether the
        // server is unreachable, the installer is missing (404), etc.
        _logger.LogError(ex, "Failed to download the agent installer from {ServerUri}.", serverUri);
        return Result.Fail($"Couldn't download the installer from the server. {ex.Message}");
      }

      var startInfo = new ProcessStartInfo(installerPath, BuildInstallArguments(serverUri, grant))
      {
        UseShellExecute = true,
        Verb = "runas",
      };

      try
      {
        // This overload throws ProcessStatusException on a non-zero exit, so reaching here means success.
        await _processManager.StartAndWaitForExit(startInfo, _installTimeout);
        return Result.Ok();
      }
      catch (ProcessStatusException ex)
      {
        _logger.LogError(ex, "The agent installer exited with a non-zero status code.");
        return Result.Fail($"The installer ran but reported an error ({ex.Message}).");
      }
      catch (OperationCanceledException)
      {
        _logger.LogWarning(
          "The agent installer did not finish within {Minutes} minutes.",
          _installTimeout.TotalMinutes);
        return Result.Fail("The installer did not finish in time. Please try again.");
      }
    }
    catch (Win32Exception ex) when (ex.NativeErrorCode == ElevationCancelledError)
    {
      _logger.LogInformation("Unattended access was declined at the elevation prompt.");
      return Result.Fail(Localization.UnattendedAccessDeclined);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to set up unattended access.");
      return Result.Fail($"{Localization.UnattendedAccessInstallFailed} ({ex.Message})");
    }
    finally
    {
      TryDelete(directory);
    }
  }

  public static string BuildInstallArguments(Uri serverUri, UnattendedAccessGrantDto grant)
  {
    // Named after the server, so this sits alongside any agent another server installed.
    var instanceId = serverUri.Host.SanitizeForFileSystem();
    return $"install -s {serverUri} -t {grant.TenantId} -ks {grant.KeySecret} -ki {grant.KeyId} -i {instanceId}";
  }

  private static async Task Download(Uri serverUri, string installerPath, CancellationToken cancellationToken)
  {
    var runtime = Environment.Is64BitOperatingSystem ? RuntimeId.WinX64 : RuntimeId.WinX86;
    var downloadUri = new Uri(serverUri, AppConstants.GetInstallerDownloadPath(runtime));

    using var httpClient = new HttpClient();
    await using var source = await httpClient.GetStreamAsync(downloadUri, cancellationToken);
    await using var destination = File.Create(installerPath);
    await source.CopyToAsync(destination, cancellationToken);
  }

  private void TryDelete(string? directory)
  {
    if (directory is null)
    {
      return;
    }

    try
    {
      Directory.Delete(directory, recursive: true);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to remove the installer directory.");
    }
  }
}
