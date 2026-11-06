using Bunit;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.Internal;
using ControlR.Libraries.Api.Contracts.Enums;
using ControlR.Libraries.Shared.Constants;
using ControlR.Web.Client.Components.Pages;
using ControlR.Web.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MudBlazor.Services;

namespace ControlR.Web.Client.Tests;

public class GetSupportPageTests
{
  [Fact]
  public async Task WhenAppIsAvailable_OffersDownloadAndServerAddress()
  {
    await using var ctx = new BunitContext();
    var cut = RenderPage(ctx, isAvailable: true);

    cut.WaitForAssertion(() => Assert.Contains("Download for Windows", cut.Markup));

    var link = cut.Find("a[href]");
    Assert.Equal(AppConstants.GetAssistDownloadPath(RuntimeId.WinX64), link.GetAttribute("href"));
    Assert.Equal("https://support.example.com", cut.Find("input").GetAttribute("value"));
  }

  [Fact]
  public async Task WhenAppIsMissing_SaysSoInsteadOfOfferingABrokenLink()
  {
    await using var ctx = new BunitContext();
    var cut = RenderPage(ctx, isAvailable: false);

    cut.WaitForAssertion(() => Assert.Contains("not available on this server", cut.Markup));

    Assert.DoesNotContain("Download for Windows", cut.Markup);
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task AlwaysWarnsAgainstUnexpectedRequests(bool isAvailable)
  {
    await using var ctx = new BunitContext();
    var cut = RenderPage(ctx, isAvailable);

    Assert.Contains("unexpected call", cut.Markup);
  }

  private static IRenderedComponent<GetSupport> RenderPage(BunitContext ctx, bool isAvailable)
  {
    var settings = new Mock<IPublicServerSettingsProvider>();
    settings
      .Setup(x => x.GetPublicServerSettings())
      .ReturnsAsync(new PublicServerSettings(false, false, IsAssistDownloadAvailable: isAvailable));

    var resolver = new Mock<IServerUriResolver>();
    resolver
      .Setup(x => x.ResolveBaseUriAsync())
      .ReturnsAsync(new Uri("https://support.example.com/"));

    ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    ctx.Services.AddMudServices();
    ctx.Services.AddSingleton(settings.Object);
    ctx.Services.AddSingleton(resolver.Object);

    return ctx.Render<GetSupport>();
  }
}
