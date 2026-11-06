using ControlR.Libraries.WebSocketRelay.Common.Sessions;
using ControlR.Web.Server.Api.V1;
using ControlR.Web.Server.Data;
using ControlR.Web.Server.Data.Entities;
using ControlR.Web.Server.Services.RemoteSessions;
using ControlR.Web.Server.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ControlR.Web.Server.Tests;

public class RemoteSessionLogTests(ITestOutputHelper testOutput)
{
  private readonly ITestOutputHelper _testOutput = testOutput;

  private static CancellationToken Ct => TestContext.Current.CancellationToken;

  [Fact]
  public async Task CleanExpiredLogs_DeletesOnlyLogsPastRetention()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var now = DateTimeOffset.UtcNow;
    var expired = await SeedLog(testApp, tenant.Id, now.AddDays(-366));
    var retained = await SeedLog(testApp, tenant.Id, now.AddDays(-364));

    var service = testApp.Services.GetRequiredService<RemoteSessionLogCleanupBackgroundService>();
    await service.CleanExpiredLogs(Ct);

    var remaining = (await GetLogs(testApp)).Select(x => x.Id).ToArray();
    Assert.DoesNotContain(expired.Id, remaining);
    Assert.Contains(retained.Id, remaining);
  }

  [Fact]
  public async Task FlagInterruptedSessions_FlagsOnlyOpenSessions_AndALateEndClearsTheFlag()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var open = await SeedLog(testApp, tenant.Id, DateTimeOffset.UtcNow);
    var ended = await SeedLog(testApp, tenant.Id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    var service = testApp.Services.GetRequiredService<RemoteSessionLogCleanupBackgroundService>();
    Assert.Equal(1, await service.FlagInterruptedSessions(Ct));

    var logs = await GetLogs(testApp);
    Assert.True(logs.Single(x => x.Id == open.Id).WasInterrupted);
    Assert.False(logs.Single(x => x.Id == ended.Id).WasInterrupted);

    await Observer(testApp).OnPeerDisconnected(open.SessionId, wasConnected: true);

    var closed = (await GetLogs(testApp)).Single(x => x.Id == open.Id);
    Assert.False(closed.WasInterrupted);
    Assert.NotNull(closed.EndedAt);
  }

  [Fact]
  public async Task GetRemoteSessionLogs_ListsOnlyTheCallersTenant_NewestFirst()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var otherTenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);
    var now = DateTimeOffset.UtcNow;
    var older = await SeedLog(testApp, tenant.Id, now.AddHours(-2));
    var newer = await SeedLog(testApp, tenant.Id, now.AddHours(-1));
    await SeedLog(testApp, otherTenant.Id, now);

    using var scope = testApp.Services.CreateScope();
    var controller = await scope.CreateControllerWithUser<RemoteSessionLogsController>(user);

    var response = await controller.GetRemoteSessionLogs(tenant.Id, deviceId: null, take: 0, Ct);

    var logs = Assert.IsType<V1Dtos.RemoteSessionLogDto[]>(Assert.IsType<OkObjectResult>(response.Result).Value);
    Assert.Equal([newer.Id, older.Id], logs.Select(x => x.Id));
  }

  [Fact]
  public async Task GetRemoteSessionLogs_OtherTenantRequested_IsForbidden()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var otherTenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);

    using var scope = testApp.Services.CreateScope();
    var controller = await scope.CreateControllerWithUser<RemoteSessionLogsController>(user);

    var response = await controller.GetRemoteSessionLogs(otherTenant.Id, deviceId: null, take: 0, Ct);

    Assert.IsType<ForbidResult>(response.Result);
  }

  [Fact]
  public async Task OnPeerDisconnected_BothPeersReport_KeepsTheFirstEndTime()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var log = await SeedLog(testApp, tenant.Id, DateTimeOffset.UtcNow.AddMinutes(-5));

    await Observer(testApp).OnPeerDisconnected(log.SessionId, wasConnected: true);
    var firstEnd = (await GetLogs(testApp)).Single().EndedAt;
    await Observer(testApp).OnPeerDisconnected(log.SessionId, wasConnected: true);

    var stored = (await GetLogs(testApp)).Single();
    Assert.NotNull(firstEnd);
    Assert.Equal(firstEnd, stored.EndedAt);
    Assert.Null(stored.FailureReason);
  }

  [Fact]
  public async Task OnPeerDisconnected_NeverPaired_RecordsAFailure()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var log = await SeedLog(testApp, tenant.Id, DateTimeOffset.UtcNow);

    await Observer(testApp).OnPeerDisconnected(log.SessionId, wasConnected: false);

    var stored = (await GetLogs(testApp)).Single();
    Assert.NotNull(stored.EndedAt);
    Assert.NotNull(stored.FailureReason);
  }

  [Fact]
  public async Task OnPeerDisconnected_UnknownSession_DoesNothing()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    await SeedLog(testApp, tenant.Id, DateTimeOffset.UtcNow);

    await Observer(testApp).OnPeerDisconnected(Guid.NewGuid(), wasConnected: true);

    Assert.Null((await GetLogs(testApp)).Single().EndedAt);
  }

  [Fact]
  public async Task LogFileOperation_LongDetail_IsStoredAsAnEndedRow()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    using var scope = testApp.Services.CreateScope();
    var controller = await scope.CreateControllerWithUser<RemoteSessionLogsController>(user);
    var stored = await scope.ServiceProvider.GetRequiredService<AppDb>().Devices
      .IgnoreQueryFilters()
      .AsNoTracking()
      .SingleAsync(x => x.Id == device.Id, Ct);

    await testApp.Services.GetRequiredService<IRemoteSessionLogger>()
      .LogFileOperation(stored, controller.User, new string('p', 2000), Ct);

    var log = (await GetLogs(testApp)).Single();
    Assert.Equal(RemoteSessionKind.FileOperation, log.Kind);
    Assert.Equal(device.Id, log.DeviceId);
    Assert.Equal(user.Id, log.UserId);
    Assert.Equal(512, log.Detail?.Length);
    Assert.NotNull(log.EndedAt);
  }

  [Fact]
  public async Task Postgres_LogLifecycle_Persists()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(
      _testOutput,
      testDatabaseName: $"{Guid.NewGuid()}",
      useInMemoryDatabase: false);
    var tenant = await testApp.Services.CreateTestTenant();
    var sessionId = Guid.NewGuid();
    var logger = testApp.Services.GetRequiredService<IRemoteSessionLogger>();

    await logger.LogStarted(
      new RemoteSessionStart(sessionId, tenant.Id, Guid.NewGuid(), "PC-1", Guid.NewGuid(), "Tech", "10.0.0.5"),
      Ct);
    await logger.LogFailed(sessionId, new string('x', 2000));

    var stored = (await GetLogs(testApp)).Single();
    Assert.Equal("PC-1", stored.DeviceName);
    Assert.Equal(512, stored.FailureReason?.Length);
    Assert.NotNull(stored.EndedAt);
  }

  private static async Task<List<RemoteSessionLog>> GetLogs(TestApp testApp)
  {
    using var scope = testApp.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    return await db.RemoteSessionLogs.IgnoreQueryFilters().AsNoTracking().ToListAsync(Ct);
  }

  private static IRelaySessionObserver Observer(TestApp testApp)
  {
    return testApp.Services.GetRequiredService<IRelaySessionObserver>();
  }

  private static async Task<RemoteSessionLog> SeedLog(
    TestApp testApp,
    Guid tenantId,
    DateTimeOffset startedAt,
    DateTimeOffset? endedAt = null)
  {
    using var scope = testApp.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    var log = new RemoteSessionLog
    {
      DeviceId = Guid.NewGuid(),
      DeviceName = "PC",
      EndedAt = endedAt,
      SessionId = Guid.NewGuid(),
      StartedAt = startedAt,
      TenantId = tenantId,
      UserId = Guid.NewGuid(),
      UserName = "Tech",
    };
    db.RemoteSessionLogs.Add(log);
    await db.SaveChangesAsync(Ct);
    return log;
  }
}
