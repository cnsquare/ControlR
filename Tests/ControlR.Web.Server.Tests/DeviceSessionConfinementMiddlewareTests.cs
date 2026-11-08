using System.Security.Claims;
using ControlR.Web.Server.Authn;
using ControlR.Web.Server.Middleware;
using Microsoft.AspNetCore.Http;

namespace ControlR.Web.Server.Tests;

public class DeviceSessionConfinementMiddlewareTests
{
  private static readonly Guid _deviceId = Guid.NewGuid();

  [Theory]
  [InlineData("/")]
  [InlineData("/settings")]
  [InlineData("/deploy")]
  [InlineData("/Account/Manage")]
  [InlineData("/account")]
  public async Task Guest_OnMainAppOrAccountPage_IsRedirectedToDeviceAccess(string path)
  {
    var (context, nextCalled) = await InvokeAsync(path, HttpMethods.Get, LogonTokenGuest());

    Assert.False(nextCalled());
    Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
    Assert.Equal($"/device-access/remote-control?deviceId={_deviceId}", context.Response.Headers.Location);
  }

  [Fact]
  public async Task Guest_NonGetToDisallowedPath_IsForbidden()
  {
    var (context, nextCalled) = await InvokeAsync("/Account/Manage/ChangePassword", HttpMethods.Post, LogonTokenGuest());

    Assert.False(nextCalled());
    Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
  }

  [Theory]
  [InlineData("/device-access/remote-control")]
  [InlineData("/api/v1/devices")]
  [InlineData("/hubs/viewer")]
  [InlineData("/relay")]
  [InlineData("/_framework/blazor.webassembly.js")]
  [InlineData("/favicon.ico")]
  [InlineData("/downloads/win-x64/EnkrOma.Assist.exe")]
  public async Task Guest_OnDeviceAccessOrInfraPath_IsAllowed(string path)
  {
    var (_, nextCalled) = await InvokeAsync(path, HttpMethods.Get, LogonTokenGuest());
    Assert.True(nextCalled());
  }

  [Theory]
  [InlineData("/")]
  [InlineData("/settings")]
  [InlineData("/Account/Manage")]
  public async Task NormalUser_IsNeverConfined(string path)
  {
    var (_, nextCalled) = await InvokeAsync(path, HttpMethods.Get, NormalUser());
    Assert.True(nextCalled());
  }

  [Fact]
  public async Task Unauthenticated_IsNeverConfined()
  {
    var (_, nextCalled) = await InvokeAsync("/settings", HttpMethods.Get, new ClaimsPrincipal(new ClaimsIdentity()));
    Assert.True(nextCalled());
  }

  private static ClaimsPrincipal LogonTokenGuest() =>
    new(new ClaimsIdentity(
      [
        new Claim(UserClaimTypes.AuthenticationMethod, PrincipalClaimValues.LogonTokenMethod),
        new Claim(UserClaimTypes.DeviceSessionScope, _deviceId.ToString()),
      ],
      authenticationType: "Cookies"));

  private static ClaimsPrincipal NormalUser() =>
    new(new ClaimsIdentity(
      [new Claim(UserClaimTypes.AuthenticationMethod, "cookie")],
      authenticationType: "Cookies"));

  private static async Task<(HttpContext Context, Func<bool> NextCalled)> InvokeAsync(
    string path,
    string method,
    ClaimsPrincipal user)
  {
    var called = false;
    var middleware = new DeviceSessionConfinementMiddleware(_ =>
    {
      called = true;
      return Task.CompletedTask;
    });

    var context = new DefaultHttpContext
    {
      User = user,
    };
    context.Request.Method = method;
    context.Request.Path = path;

    await middleware.Invoke(context);
    return (context, () => called);
  }
}
