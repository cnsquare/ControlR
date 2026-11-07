using System.Diagnostics.CodeAnalysis;

namespace ControlR.Libraries.Branding;

/// <summary>
/// Centralized branding constants. These values are replaced by the build script during customized builds.
/// The derived names come from <see cref="BrandNames"/>, so a build can also ask for another brand's
/// names without a second copy of the formulas.
/// </summary>
[SuppressMessage("MemberOrder", "BB0001", Justification = "Constants are grouped by category, not by member type.")]
public static class BrandingConstants
{
  public const string BrandName = "ControlR";
  public const string Publisher = "Bitbound";

  /// <summary>
  /// Reduces a brand name to the filesystem-safe form used for directory, service, and registry names.
  /// Two names that share a key address the same install, so compare keys rather than names.
  /// </summary>
  public static string SanitizeBrandKey(string brandName) => BrandNames.SanitizeBrandKey(brandName);

  public const string PrimaryColorDark = "2196F3";
  public const string SecondaryColorDark = "21f3e9";
  public const string TertiaryColorDark = "7b21f3";
  public const string InfoColorDark = "89b4f8";
  public const string SuccessColorDark = "2cb67d";
  public const string WarningColorDark = "facc15";
  public const string ErrorColorDark = "f87171";

  public const string PrimaryColorLight = "2196F3";
  public const string SecondaryColorLight = "008c7a";
  public const string TertiaryColorLight = "7b21f3";
  public const string InfoColorLight = "0d6efd";
  public const string SuccessColorLight = "28a745";
  public const string WarningColorLight = "ffc107";
  public const string ErrorColorLight = "dc3545";

  /// <summary>
  /// Where to read the upstream MIT license. A branded build serves its own copy.
  /// </summary>
  public static string LicenseUrl =>
    ControlrServerUrl is { } serverUrl
      ? new Uri(serverUrl, "static/ControlR-LICENSE.txt").ToString()
      : "https://github.com/bitbound/ControlR/blob/main/LICENSE.txt";

  public static string BrandKey => BrandNames.Current.BrandKey;
  public static string UnixBrandKey => BrandNames.Current.UnixBrandKey;

  private static Uri? ParseControlrServerUrl(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return null;
    }

    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
      || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
    {
      throw new InvalidOperationException(
        $"The baked-in ControlR server URL must be an absolute http/https URL. Got: '{value}'.");
    }

    return uri;
  }

  /// <summary>
  /// The server URL baked into a customized build. Null in the default build.
  /// </summary>
  public static Uri? ControlrServerUrl { get; } = ParseControlrServerUrl(null);

  /// <summary>
  /// Shortest fleet secret a build is allowed to bake in. Matches the server's own minimum so a
  /// too-short brand secret fails the build rather than quietly weakening re-enrollment.
  /// </summary>
  public const int MinFleetEnrollmentSecretLength = 16;

  private static string? ParseFleetEnrollmentSecret(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return null;
    }

    var trimmed = value.Trim();
    if (trimmed.Length < MinFleetEnrollmentSecretLength)
    {
      throw new InvalidOperationException(
        $"The baked-in fleet enrollment secret must be at least {MinFleetEnrollmentSecretLength} characters.");
    }

    return trimmed;
  }

  /// <summary>
  /// A fleet-wide secret baked into a customized build. When set, agents of this build re-create
  /// their own device records on a server of the same build after its database is lost and rebuilt,
  /// with no per-install configuration. Null in the default build, which leaves re-enrollment off
  /// unless a secret is supplied by server config and the installer's --fleet-secret option. A
  /// per-install or server-config secret always overrides this baked-in default. Treat it as a
  /// fleet-wide credential: anyone holding it and an agent config could re-create a device.
  /// </summary>
  public static string? FleetEnrollmentSecret { get; } = ParseFleetEnrollmentSecret(null);

  /// <summary>
  /// Hides the Project, Website, and Sponsor links in the built app UI. False in the default build.
  /// </summary>
  public static bool HideSponsorshipInfo { get; } = false;

  public static string AuthenticatorIssuerName => BrandName;

  public static string WindowsInstallDirectoryName => BrandNames.Current.BrandKey;
  public static string LinuxInstallDirectoryName => BrandNames.Current.BrandKey;
  public static string MacInstallDirectoryName => BrandNames.Current.BrandKey;

  public static string MacAppBundleBaseName => BrandNames.Current.MacAppBundleBaseName;
  public static string MacBundleStateDirectoryName => BrandNames.Current.MacBundleStateDirectoryName;
  public static string UpdaterTempDirectoryName => BrandNames.Current.UpdaterTempDirectoryName;

  public static string AgentBaseName => BrandNames.Current.AgentBaseName;
  public static string AssistBaseName => BrandNames.Current.AssistBaseName;
  public static string DesktopClientBaseName => BrandNames.Current.DesktopClientBaseName;
  public static string InstallerBaseName => BrandNames.Current.InstallerBaseName;
  public static string WebServerAssemblyName => "ControlR.Web.Server";
  public static string BundleZipBaseName => BrandNames.Current.BundleZipBaseName;
  public static string DesktopClientDirectoryName => "DesktopClient";

  public static string WindowsLogDirectoryName => BrandNames.Current.WindowsLogDirectoryName;
  public static string UnixLogDirectoryName => BrandNames.Current.UnixLogDirectoryName;
  public static string UnixConfigDirectoryName => BrandNames.Current.UnixConfigDirectoryName;
  public static string UnixHiddenDirectoryName => BrandNames.Current.UnixHiddenDirectoryName;

  public static string WindowsServiceBaseName => BrandNames.Current.WindowsServiceBaseName;
  public static string LinuxAgentServiceName => BrandNames.Current.LinuxAgentServiceName;
  public static string LinuxDesktopServiceName => BrandNames.Current.LinuxDesktopServiceName;
  public static string MacServicePrefix => BrandNames.Current.MacServicePrefix;

  public static string WindowsUninstallRegistryKeyName => BrandNames.Current.WindowsUninstallRegistryKeyName;

  public static string BundleHashFileName => BrandNames.Current.BundleHashFileName;
  public static string RepairStageDirectoryPrefix => BrandNames.Current.RepairStageDirectoryPrefix;

  public static string IpcPipeBaseName => BrandNames.Current.IpcPipeBaseName;
}
