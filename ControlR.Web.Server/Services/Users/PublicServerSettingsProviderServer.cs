using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.Internal;
using ControlR.Web.Client.Services;

namespace ControlR.Web.Server.Services.Users;

internal class PublicServerSettingsProviderServer(
  IDbContextFactory<AppDb> dbFactory,
  IOptionsMonitor<AppOptions> appOptions,
  ILogger<PublicServerSettingsProviderServer> logger,
  IPublicUrlProvider publicUrlProvider,
  IWebHostEnvironment webHostEnvironment) : IPublicServerSettingsProvider
{
  private readonly IOptionsMonitor<AppOptions> _appOptions = appOptions;
  private readonly IDbContextFactory<AppDb> _dbFactory = dbFactory;
  private readonly ILogger<PublicServerSettingsProviderServer> _logger = logger;
  private readonly IPublicUrlProvider _publicUrlProvider = publicUrlProvider;
  private readonly IWebHostEnvironment _webHostEnvironment = webHostEnvironment;

  public async Task<PublicServerSettings> GetPublicServerSettings()
  {
    try
    {
      var appOpts = _appOptions.CurrentValue;
      await using var db = await _dbFactory.CreateDbContextAsync();
      var hasUsers = await db.Users.AnyAsync();
      var registrationEnabled = appOpts.EnablePublicRegistration ||
        (!appOpts.DisableFirstUserSelfRegistration && !hasUsers);

      return new PublicServerSettings(
        IsPublicRegistrationEnabled: registrationEnabled,
        DisableDesktopPreview: appOpts.DisableDesktopPreview,
        PublicBaseUrl: _publicUrlProvider.TryGetBaseUrl(),
        DisableRemoteAudio: appOpts.DisableRemoteAudio,
        IsAssistDownloadAvailable: IsAssistDownloadAvailable());
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error while getting public server settings.");
      return new PublicServerSettings(
        IsPublicRegistrationEnabled: false,
        DisableDesktopPreview: _appOptions.CurrentValue.DisableDesktopPreview,
        PublicBaseUrl: _publicUrlProvider.TryGetBaseUrl(),
        DisableRemoteAudio: _appOptions.CurrentValue.DisableRemoteAudio,
        IsAssistDownloadAvailable: IsAssistDownloadAvailable());
    }
  }

  // Only the release pipeline puts the app on the server, so it can legitimately be absent.
  private bool IsAssistDownloadAvailable()
  {
    var path = AppConstants.GetAssistDownloadPath(RuntimeId.WinX64);
    return _webHostEnvironment.WebRootFileProvider.GetFileInfo(path).Exists;
  }
}
