using System.Net;
using ControlR.Libraries.Api.Contracts.Dtos.Devices;
using ControlR.Web.Server.Api.V1;
using ControlR.Web.Server.Data;
using ControlR.Web.Server.Data.Entities;
using ControlR.Web.Server.Services.Alerts;
using ControlR.Web.Server.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ControlR.Web.Server.Tests;

public class DeviceAlertTests(ITestOutputHelper testOutput)
{
  private readonly ITestOutputHelper _testOutput = testOutput;

  private static CancellationToken Ct => TestContext.Current.CancellationToken;

  [Fact]
  public async Task CheckDevices_DismissedAlert_IsNotRaisedAgainWhileTheProblemLasts()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    await SetDevice(testApp, device.Id, isOnline: false, lastSeen: DateTimeOffset.UtcNow.AddHours(-2));
    await SaveSettings(testApp, tenant.Id, offlineMinutes: 30);

    Assert.Equal(1, await Check(testApp));

    await UpdateAlerts(testApp, x => x.DismissedAt = DateTimeOffset.UtcNow);

    Assert.Equal(0, await Check(testApp));
    Assert.Single(await GetAlerts(testApp));
  }

  [Fact]
  public async Task CheckDevices_DeviceOfflinePastThreshold_RaisesOnceAndResolvesWhenBack()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    await SetDevice(testApp, device.Id, isOnline: false, lastSeen: DateTimeOffset.UtcNow.AddHours(-2));
    await SaveSettings(testApp, tenant.Id, offlineMinutes: 30);

    Assert.Equal(1, await Check(testApp));
    Assert.Equal(0, await Check(testApp));

    await SetDevice(testApp, device.Id, isOnline: true, lastSeen: DateTimeOffset.UtcNow);
    await Check(testApp);

    Assert.NotNull(Assert.Single(await GetAlerts(testApp)).ResolvedAt);
  }

  [Fact]
  public async Task CheckDevices_DeviceOfflineUnderThreshold_RaisesNothing()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    await SetDevice(testApp, device.Id, isOnline: false, lastSeen: DateTimeOffset.UtcNow.AddMinutes(-10));
    await SaveSettings(testApp, tenant.Id, offlineMinutes: 30);

    Assert.Equal(0, await Check(testApp));
  }

  [Fact]
  public async Task CheckDevices_LowDisk_RaisesAndResolvesWhenSpaceReturns()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    await SetDevice(testApp, device.Id, isOnline: true, lastSeen: DateTimeOffset.UtcNow, freeSpace: 5, totalSize: 100);
    await SaveSettings(testApp, tenant.Id, lowDiskPercent: 10);

    Assert.Equal(1, await Check(testApp));
    Assert.Contains("5%", Assert.Single(await GetAlerts(testApp)).Message);

    await SetDevice(testApp, device.Id, isOnline: true, lastSeen: DateTimeOffset.UtcNow, freeSpace: 50, totalSize: 100);
    await Check(testApp);

    Assert.NotNull(Assert.Single(await GetAlerts(testApp)).ResolvedAt);
  }

  [Fact]
  public async Task CheckDevices_LowDiskOnOfflineDevice_NeitherRaisesNorResolves()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    await SetDevice(testApp, device.Id, isOnline: false, lastSeen: DateTimeOffset.UtcNow, freeSpace: 5, totalSize: 100);
    await SaveSettings(testApp, tenant.Id, lowDiskPercent: 10);

    Assert.Equal(0, await Check(testApp));

    await SetDevice(testApp, device.Id, isOnline: true, lastSeen: DateTimeOffset.UtcNow, freeSpace: 5, totalSize: 100);
    Assert.Equal(1, await Check(testApp));

    await SetDevice(testApp, device.Id, isOnline: false, lastSeen: DateTimeOffset.UtcNow, freeSpace: 50, totalSize: 100);
    await Check(testApp);

    Assert.Null(Assert.Single(await GetAlerts(testApp)).ResolvedAt);
  }

  [Fact]
  public async Task CheckDevices_NoSettings_RaisesNothing()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    await SetDevice(testApp, device.Id, isOnline: false, lastSeen: DateTimeOffset.UtcNow.AddDays(-30));

    Assert.Equal(0, await Check(testApp));
  }

  [Fact]
  public async Task CheckDevices_ThresholdTurnedOff_ResolvesOpenAlerts()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    await SetDevice(testApp, device.Id, isOnline: false, lastSeen: DateTimeOffset.UtcNow.AddHours(-2));
    await SaveSettings(testApp, tenant.Id, offlineMinutes: 30);
    await Check(testApp);

    await SaveSettings(testApp, tenant.Id, offlineMinutes: 0);
    await Check(testApp);

    Assert.NotNull(Assert.Single(await GetAlerts(testApp)).ResolvedAt);
  }

  [Theory]
  [InlineData("127.0.0.1")]
  [InlineData("10.1.2.3")]
  [InlineData("172.16.0.1")]
  [InlineData("172.31.255.1")]
  [InlineData("192.168.1.1")]
  [InlineData("169.254.169.254")]
  [InlineData("100.64.0.1")]
  [InlineData("0.0.0.0")]
  [InlineData("224.0.0.1")]
  [InlineData("::1")]
  [InlineData("fe80::1")]
  [InlineData("fc00::1")]
  [InlineData("fd12::1")]
  [InlineData("::ffff:10.0.0.1")]
  [InlineData("::ffff:169.254.169.254")]
  public void IsAllowedAddress_InternalAddress_IsRefused(string address)
  {
    Assert.False(WebhookTargetPolicy.IsAllowedAddress(IPAddress.Parse(address)));
  }

  [Theory]
  [InlineData("8.8.8.8")]
  [InlineData("172.32.0.1")]
  [InlineData("2606:4700:4700::1111")]
  public void IsAllowedAddress_PublicAddress_IsAllowed(string address)
  {
    Assert.True(WebhookTargetPolicy.IsAllowedAddress(IPAddress.Parse(address)));
  }

  [Theory]
  [InlineData("https://hooks.example.com/abc", false, true)]
  [InlineData("http://hooks.example.com/abc", false, false)]
  [InlineData("https://localhost/abc", false, false)]
  [InlineData("https://10.0.0.1/abc", false, false)]
  [InlineData("https://[::1]/abc", false, false)]
  [InlineData("https://user:pass@hooks.example.com/", false, false)]
  [InlineData("ftp://hooks.example.com/", false, false)]
  [InlineData("not a url", false, false)]
  [InlineData("http://localhost:9000/hook", true, true)]
  [InlineData("ftp://localhost/", true, false)]
  public void IsAllowedUrl_ChecksSchemeAndHost(string url, bool allowPrivate, bool expected)
  {
    Assert.Equal(expected, WebhookTargetPolicy.IsAllowedUrl(url, allowPrivate));
  }

  [Fact]
  public async Task UpdateAlertSettings_InternalWebhook_IsRefused()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);

    using var scope = testApp.Services.CreateScope();
    var controller = await scope.CreateControllerWithUser<DeviceAlertsController>(user);

    var response = await controller.UpdateAlertSettings(
      new(0, 0, null, "https://169.254.169.254/latest/meta-data"),
      tenant.Id,
      Ct);

    ActionResultAsserts.AssertHttpStatus(response.Result, 400);
  }

  [Fact]
  public async Task UpdateAlertSettings_OtherTenant_IsForbidden()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var otherTenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);

    using var scope = testApp.Services.CreateScope();
    var controller = await scope.CreateControllerWithUser<DeviceAlertsController>(user);

    var response = await controller.UpdateAlertSettings(new(5, 5, null, null), otherTenant.Id, Ct);

    Assert.IsType<ForbidResult>(response.Result);
  }

  [Fact]
  public async Task Postgres_SettingsSaveAndUpdateInPlace()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(
      _testOutput,
      testDatabaseName: $"{Guid.NewGuid()}",
      useInMemoryDatabase: false);
    var tenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);

    using var scope = testApp.Services.CreateScope();
    var controller = await scope.CreateControllerWithUser<DeviceAlertsController>(user);

    await controller.UpdateAlertSettings(new(30, 10, "ops@example.com", "https://hooks.example.com/a"), tenant.Id, Ct);
    await controller.UpdateAlertSettings(new(60, 0, null, null), tenant.Id, Ct);

    using var readScope = testApp.Services.CreateScope();
    var db = readScope.ServiceProvider.GetRequiredService<AppDb>();
    var stored = await db.AlertSettings.IgnoreQueryFilters().AsNoTracking().SingleAsync(Ct);
    Assert.Equal(60, stored.OfflineMinutes);
    Assert.Null(stored.WebhookUrl);
  }

  private static Task<int> Check(TestApp testApp)
  {
    return testApp.Services.GetRequiredService<DeviceHealthBackgroundService>().CheckDevices(Ct);
  }

  private static async Task<List<DeviceAlert>> GetAlerts(TestApp testApp)
  {
    using var scope = testApp.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    return await db.DeviceAlerts.IgnoreQueryFilters().AsNoTracking().ToListAsync(Ct);
  }

  private static async Task SaveSettings(TestApp testApp, Guid tenantId, int offlineMinutes = 0, int lowDiskPercent = 0)
  {
    using var scope = testApp.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    var settings = await db.AlertSettings.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TenantId == tenantId, Ct);
    if (settings is null)
    {
      settings = new AlertSettings { Id = Guid.NewGuid(), TenantId = tenantId };
      db.AlertSettings.Add(settings);
    }

    settings.LowDiskPercent = lowDiskPercent;
    settings.OfflineMinutes = offlineMinutes;
    await db.SaveChangesAsync(Ct);
  }

  private static async Task SetDevice(
    TestApp testApp,
    Guid deviceId,
    bool isOnline,
    DateTimeOffset lastSeen,
    double? freeSpace = null,
    double? totalSize = null)
  {
    using var scope = testApp.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    var device = await db.Devices.IgnoreQueryFilters().SingleAsync(x => x.Id == deviceId, Ct);
    device.IsOnline = isOnline;
    device.LastSeen = lastSeen;

    if (freeSpace is not null && totalSize is not null)
    {
      device.Drives =
      [
        new Drive
        {
          DriveType = DriveType.Fixed,
          FreeSpace = freeSpace.Value,
          Name = "C:",
          RootDirectory = "C:\\",
          TotalSize = totalSize.Value,
        },
      ];
    }

    await db.SaveChangesAsync(Ct);
  }

  private static async Task UpdateAlerts(TestApp testApp, Action<DeviceAlert> update)
  {
    using var scope = testApp.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    foreach (var alert in await db.DeviceAlerts.IgnoreQueryFilters().ToListAsync(Ct))
    {
      update(alert);
    }

    await db.SaveChangesAsync(Ct);
  }
}
