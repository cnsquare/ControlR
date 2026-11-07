using System.Diagnostics;
using ControlR.Libraries.Shared.Constants;
using Microsoft.Extensions.Logging;

namespace ControlR.DesktopClient.Common.Services.Encoders;

public interface IFfmpegLocator
{
  /// <summary>
  /// Returns the path of an ffmpeg that can encode VP9, or null if there isn't one.
  /// </summary>
  string? FindVp9Capable();
}

/// <summary>
/// Prefers a copy beside the desktop client, because that folder is protected on an installed
/// device. A copy found on the PATH is only as trustworthy as that PATH.
/// </summary>
public class FfmpegLocator(ILogger<FfmpegLocator> logger) : IFfmpegLocator
{
  private static readonly TimeSpan _probeTimeout = TimeSpan.FromSeconds(5);

  private readonly ILogger<FfmpegLocator> _logger = logger;

  public string? FindVp9Capable()
  {
    foreach (var candidate in GetCandidates())
    {
      if (File.Exists(candidate) && CanEncodeVp9(candidate))
      {
        return candidate;
      }
    }

    return null;
  }

  private static IEnumerable<string> GetCandidates()
  {
    var fileName = AppConstants.FfmpegFileName;
    yield return Path.Combine(AppContext.BaseDirectory, fileName);

    var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
    foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
    {
      // A relative entry would resolve against the working directory.
      if (Path.IsPathFullyQualified(directory))
      {
        yield return Path.Combine(directory, fileName);
      }
    }
  }

  private bool CanEncodeVp9(string path)
  {
    try
    {
      using var process = Process.Start(new ProcessStartInfo
      {
        FileName = path,
        ArgumentList = { "-hide_banner", "-encoders" },
        CreateNoWindow = true,
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        UseShellExecute = false,
      });

      if (process is null)
      {
        return false;
      }

      process.ErrorDataReceived += (_, _) => { };
      process.BeginErrorReadLine();

      var output = process.StandardOutput.ReadToEndAsync();
      if (!output.Wait(_probeTimeout))
      {
        process.Kill(entireProcessTree: true);
        return false;
      }

      return output.Result.Contains("libvpx-vp9", StringComparison.Ordinal);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to check ffmpeg at {Path}.", path);
      return false;
    }
  }
}
