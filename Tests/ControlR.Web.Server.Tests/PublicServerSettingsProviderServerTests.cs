using ControlR.Libraries.Shared.Constants;
using ControlR.Web.Server.Data;
using ControlR.Web.Server.Options;
using ControlR.Web.Server.Services;
using ControlR.Web.Server.Services.Users;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ControlR.Web.Client.Services;
using ControlR.Web.Server.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace ControlR.Web.Server.Tests;

/// <summary>
/// Pins that the public base URL surfaced in <c>PublicServerSettings</c> comes from the configured
/// <c>AppOptions:PublicBaseUrl</c> through <c>IPublicUrlProvider</c>, and stays null when unset.
/// </summary>
public class PublicServerSettingsProviderServerTests(ITestOutputHelper testOutput)
{
  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task GetPublicServerSettings_ReportsWhetherTheAssistAppIsOnTheServer(bool exists)
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(testOutput);
    using var scope = testApp.CreateScope();
    var services = scope.ServiceProvider;

    var fileInfo = new Mock<IFileInfo>();
    fileInfo.SetupGet(x => x.Exists).Returns(exists);
    var missingFile = new Mock<IFileInfo>();
    missingFile.SetupGet(x => x.Exists).Returns(false);

    var files = new Mock<IFileProvider>();
    files.Setup(x => x.GetFileInfo(It.IsAny<string>())).Returns(missingFile.Object);
    files
      .Setup(x => x.GetFileInfo(AppConstants.GetAssistDownloadPath(RuntimeId.WinX64)))
      .Returns(fileInfo.Object);

    var environment = new Mock<IWebHostEnvironment>();
    environment.SetupGet(x => x.WebRootFileProvider).Returns(files.Object);

    var sut = new PublicServerSettingsProviderServer(
      services.GetRequiredService<IDbContextFactory<AppDb>>(),
      services.GetRequiredService<IOptionsMonitor<AppOptions>>(),
      NullLogger<PublicServerSettingsProviderServer>.Instance,
      services.GetRequiredService<IPublicUrlProvider>(),
      environment.Object);

    var settings = await sut.GetPublicServerSettings();

    Assert.Equal(exists, settings.IsAssistDownloadAvailable);
  }

  [Fact]
  public async Task GetPublicServerSettings_WhenPublicBaseUrlConfigured_PopulatesPublicBaseUrl()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(
      testOutput,
      extraConfiguration: new Dictionary<string, string?> { ["AppOptions:PublicBaseUrl"] = "https://controlr.test" });

    using var scope = testApp.CreateScope();
    var provider = scope.ServiceProvider.GetRequiredService<IPublicServerSettingsProvider>();

    var settings = await provider.GetPublicServerSettings();

    Assert.Equal("https://controlr.test", settings.PublicBaseUrl);
  }

  [Fact]
  public async Task GetPublicServerSettings_WhenPublicBaseUrlNotConfigured_LeavesPublicBaseUrlNull()
  {
    // appsettings.Testing.json, which the harness loads, sets a usable PublicBaseUrl. Simulating an
    // install that never configured one requires overriding it with the empty string.
    await using var testApp = await TestAppBuilder.CreateTestApp(
      testOutput,
      extraConfiguration: new Dictionary<string, string?> { ["AppOptions:PublicBaseUrl"] = "" });

    using var scope = testApp.CreateScope();
    var provider = scope.ServiceProvider.GetRequiredService<IPublicServerSettingsProvider>();

    var settings = await provider.GetPublicServerSettings();

    Assert.Null(settings.PublicBaseUrl);
  }
}
