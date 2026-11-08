using ControlR.Libraries.Api.Contracts.Dtos.HubDtos;
using ControlR.Libraries.Api.Contracts.Hubs.Clients;
using ControlR.Web.Server.Api.V1;
using ControlR.Web.Server.Authz.Permissions;
using ControlR.Web.Server.Data;
using ControlR.Web.Server.Data.Entities;
using ControlR.Web.Server.Hubs;
using ControlR.Web.Server.Services.Authorization;
using ControlR.Web.Server.Services.Scripting;
using ControlR.Web.Server.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ControlR.Web.Server.Tests;

public class ScriptQueueTests(ITestOutputHelper testOutput)
{
  private readonly ITestOutputHelper _testOutput = testOutput;

  private static CancellationToken Ct => TestContext.Current.CancellationToken;

  [Fact]
  public async Task CancelQueuedScriptRun_StopsItRunning()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    await testApp.Services.SeedServerAdminAsync();
    var fixture = await CreateFixture(testApp, isOnline: true);
    var (resultId, runId) = await SeedQueued(testApp, fixture);

    using (var scope = testApp.Services.CreateScope())
    {
      var controller = await scope.CreateControllerWithUser<ScriptRunsController>(fixture.User);
      Assert.IsType<NoContentResult>(await controller.CancelQueuedScriptRun(runId, fixture.TenantId, Ct));
    }

    var agent = new Mock<IAgentHubClient>(MockBehavior.Strict);
    await Dispatch(testApp, agent);

    var stored = await Read(testApp, resultId);
    Assert.Equal(ScriptResultStatus.Failed, stored.Status);
    Assert.Contains("Cancelled by", stored.FailureReason);
    agent.Verify(x => x.RunScript(It.IsAny<RunScriptRequestDto>()), Times.Never);
  }

  [Fact]
  public async Task CancelQueuedScriptRun_WithoutDeviceAccess_LeavesItQueued()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    await testApp.Services.SeedServerAdminAsync();
    var fixture = await CreateFixture(testApp, isOnline: false);
    var (resultId, runId) = await SeedQueued(testApp, fixture);
    var other = await testApp.Services.CreateTestUser(fixture.TenantId, email: $"o-{Guid.NewGuid():N}@t.local");

    using var scope = testApp.Services.CreateScope();
    var controller = await scope.CreateControllerWithUser<ScriptRunsController>(other);

    var response = await controller.CancelQueuedScriptRun(runId, fixture.TenantId, Ct);

    ActionResultAsserts.AssertHttpStatus(response, 404);
    Assert.Equal(ScriptResultStatus.Queued, (await Read(testApp, resultId)).Status);
  }

  [Fact]
  public async Task CreateScriptRun_OfflineDevice_WithFlag_IsQueuedNotFailed()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    await testApp.Services.SeedServerAdminAsync();
    var fixture = await CreateFixture(testApp, isOnline: false);

    using var scope = testApp.Services.CreateScope();
    var controller = await scope.CreateControllerWithUser<ScriptRunsController>(fixture.User);
    var request = new V1Dtos.CreateScriptRunRequestDto(
      [fixture.DeviceId], ScriptShell.Bash, "echo hi", RunOnNextConnect: true);

    var response = await controller.CreateScriptRun(request, fixture.TenantId, Ct);

    var run = Assert.IsType<V1Dtos.ScriptRunDto>(Assert.IsType<OkObjectResult>(response.Result).Value);
    var result = Assert.Single(run.Results);
    Assert.Equal(ScriptResultStatus.Queued, result.Status);
    Assert.Null(result.FailureReason);
  }

  [Fact]
  public async Task CreateScriptRun_OfflineDevice_WithoutFlag_StillFails()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    await testApp.Services.SeedServerAdminAsync();
    var fixture = await CreateFixture(testApp, isOnline: false);

    using var scope = testApp.Services.CreateScope();
    var controller = await scope.CreateControllerWithUser<ScriptRunsController>(fixture.User);
    var request = new V1Dtos.CreateScriptRunRequestDto([fixture.DeviceId], ScriptShell.Bash, "echo hi");

    var response = await controller.CreateScriptRun(request, fixture.TenantId, Ct);

    var run = Assert.IsType<V1Dtos.ScriptRunDto>(Assert.IsType<OkObjectResult>(response.Result).Value);
    Assert.Equal(ScriptResultStatus.Failed, Assert.Single(run.Results).Status);
  }

  [Fact]
  public async Task DispatchQueued_AgentDeclines_GoesBackInTheQueue()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    await testApp.Services.SeedServerAdminAsync();
    var fixture = await CreateFixture(testApp, isOnline: true);
    var (resultId, _) = await SeedQueued(testApp, fixture);
    var agent = Agent(HubResult.Fail("Busy."));

    await Dispatch(testApp, agent);

    var stored = await Read(testApp, resultId);
    Assert.Equal(ScriptResultStatus.Queued, stored.Status);
    Assert.Null(stored.DispatchedAt);
  }

  [Fact]
  public async Task DispatchQueued_AgentTimesOut_IsNotRetriedBecauseItMayHaveRun()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    await testApp.Services.SeedServerAdminAsync();
    var fixture = await CreateFixture(testApp, isOnline: true);
    var (resultId, _) = await SeedQueued(testApp, fixture);
    var agent = new Mock<IAgentHubClient>();
    agent.Setup(x => x.RunScript(It.IsAny<RunScriptRequestDto>())).ThrowsAsync(new TimeoutException());

    await Dispatch(testApp, agent);
    await Dispatch(testApp, agent);

    Assert.Equal(ScriptResultStatus.Failed, (await Read(testApp, resultId)).Status);
    agent.Verify(x => x.RunScript(It.IsAny<RunScriptRequestDto>()), Times.Once);
  }

  [Fact]
  public async Task DispatchQueued_DeviceOnline_SendsItOnce()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    await testApp.Services.SeedServerAdminAsync();
    var fixture = await CreateFixture(testApp, isOnline: true);
    var (resultId, _) = await SeedQueued(testApp, fixture);
    var agent = Agent(HubResult.Ok());

    await Dispatch(testApp, agent);
    await Dispatch(testApp, agent);

    var stored = await Read(testApp, resultId);
    Assert.Equal(ScriptResultStatus.Pending, stored.Status);
    Assert.NotNull(stored.DispatchedAt);
    agent.Verify(x => x.RunScript(It.Is<RunScriptRequestDto>(r => r.ScriptResultId == resultId)), Times.Once);
  }

  [Fact]
  public async Task DispatchQueued_DeviceStillOffline_KeepsWaiting()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    await testApp.Services.SeedServerAdminAsync();
    var fixture = await CreateFixture(testApp, isOnline: false);
    var (resultId, _) = await SeedQueued(testApp, fixture);
    var agent = new Mock<IAgentHubClient>(MockBehavior.Strict);

    await Dispatch(testApp, agent);

    Assert.Equal(ScriptResultStatus.Queued, (await Read(testApp, resultId)).Status);
  }

  [Fact]
  public async Task DispatchQueued_PastTheLifetime_FailsWithoutRunning()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    await testApp.Services.SeedServerAdminAsync();
    var fixture = await CreateFixture(testApp, isOnline: true);
    var now = testApp.Services.GetRequiredService<TimeProvider>().GetUtcNow();
    var (resultId, _) = await SeedQueued(testApp, fixture, createdAt: now - ScriptRunManager.QueueLifetime - TimeSpan.FromMinutes(1));
    var agent = new Mock<IAgentHubClient>(MockBehavior.Strict);

    await Dispatch(testApp, agent);

    var stored = await Read(testApp, resultId);
    Assert.Equal(ScriptResultStatus.Failed, stored.Status);
    Assert.Equal(ScriptRunManager.QueueExpiredReason, stored.FailureReason);
  }

  [Fact]
  public async Task DispatchQueued_RequesterLostPermission_FailsWithoutRunning()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    await testApp.Services.SeedServerAdminAsync();
    var fixture = await CreateFixture(testApp, isOnline: true, grant: false);
    var (resultId, _) = await SeedQueued(testApp, fixture);
    var agent = new Mock<IAgentHubClient>(MockBehavior.Strict);

    await Dispatch(testApp, agent);

    var stored = await Read(testApp, resultId);
    Assert.Equal(ScriptResultStatus.Failed, stored.Status);
    Assert.Contains("can no longer run", stored.FailureReason);
  }

  [Fact]
  public async Task DispatchQueued_ScheduleDeleted_FailsWithoutRunning()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    await testApp.Services.SeedServerAdminAsync();
    var fixture = await CreateFixture(testApp, isOnline: true);
    var (resultId, _) = await SeedQueued(testApp, fixture, scheduleId: Guid.NewGuid());
    var agent = new Mock<IAgentHubClient>(MockBehavior.Strict);

    await Dispatch(testApp, agent);

    var stored = await Read(testApp, resultId);
    Assert.Equal(ScriptResultStatus.Failed, stored.Status);
    Assert.Contains("schedule", stored.FailureReason);
  }

  [Fact]
  public async Task ExpireStaleResults_RecentlyDispatchedFromAnOldRun_IsLeftAlone()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    await testApp.Services.SeedServerAdminAsync();
    var fixture = await CreateFixture(testApp, isOnline: true);
    var now = testApp.Services.GetRequiredService<TimeProvider>().GetUtcNow();
    var (resultId, _) = await SeedQueued(testApp, fixture, createdAt: now.AddDays(-3));

    using (var scope = testApp.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<AppDb>();
      var result = await db.ScriptResults.IgnoreQueryFilters().SingleAsync(x => x.Id == resultId, Ct);
      result.DispatchedAt = now;
      result.Status = ScriptResultStatus.Pending;
      await db.SaveChangesAsync(Ct);
    }

    await testApp.Services.GetRequiredService<ScriptResultExpiryBackgroundService>().ExpireStaleResults(Ct);

    Assert.Equal(ScriptResultStatus.Pending, (await Read(testApp, resultId)).Status);
  }

  [Fact]
  public async Task Postgres_DispatchQueued_TranslatesAndSends()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(
      _testOutput,
      testDatabaseName: $"{Guid.NewGuid()}",
      useInMemoryDatabase: false);
    var fixture = await CreateFixture(testApp, isOnline: true);
    var (resultId, _) = await SeedQueued(testApp, fixture);
    var agent = Agent(HubResult.Ok());

    await Dispatch(testApp, agent);

    Assert.Equal(ScriptResultStatus.Pending, (await Read(testApp, resultId)).Status);
  }

  [Fact]
  public async Task Schedule_OfflineDeviceAcrossManyRuns_QueuesOnlyOneCopy()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    await testApp.Services.SeedServerAdminAsync();
    var fixture = await CreateFixture(testApp, isOnline: false);
    var scheduleId = Guid.NewGuid();
    var now = testApp.Services.GetRequiredService<TimeProvider>().GetUtcNow();

    using (var scope = testApp.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<AppDb>();
      db.ScriptSchedules.Add(new ScriptSchedule
      {
        Id = scheduleId,
        Content = "echo hi",
        CreatedBy = "seed",
        CreatedByUserId = fixture.User.Id,
        DeviceIds = [fixture.DeviceId],
        Interval = ScriptScheduleInterval.Hourly,
        Name = "Hourly",
        NextRun = now.AddMinutes(-1),
        RunOnNextConnect = true,
        ScriptName = "copy",
        Shell = ScriptShell.Bash,
        TenantId = fixture.TenantId,
        TimeoutSeconds = 30,
      });
      await db.SaveChangesAsync(Ct);
    }

    var service = testApp.Services.GetRequiredService<ScriptScheduleBackgroundService>();
    for (var i = 0; i < 3; i++)
    {
      await service.RunDueSchedules(Ct);

      using var scope = testApp.Services.CreateScope();
      var db = scope.ServiceProvider.GetRequiredService<AppDb>();
      var schedule = await db.ScriptSchedules.IgnoreQueryFilters().SingleAsync(x => x.Id == scheduleId, Ct);
      schedule.NextRun = now.AddMinutes(-1);
      await db.SaveChangesAsync(Ct);
    }

    using var assertScope = testApp.Services.CreateScope();
    var assertDb = assertScope.ServiceProvider.GetRequiredService<AppDb>();
    var result = await assertDb.ScriptResults.IgnoreQueryFilters().SingleAsync(Ct);
    Assert.Equal(ScriptResultStatus.Queued, result.Status);
  }

  private static Mock<IAgentHubClient> Agent(HubResult ack)
  {
    var agent = new Mock<IAgentHubClient>();
    agent.Setup(x => x.RunScript(It.IsAny<RunScriptRequestDto>())).ReturnsAsync(ack);
    return agent;
  }

  private static async Task<Fixture> CreateFixture(TestApp testApp, bool isOnline, bool grant = true)
  {
    var tenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);
    var device = await testApp.Services.CreateTestDevice(tenant.Id);

    using var scope = testApp.Services.CreateScope();
    await using var db = scope.ServiceProvider.GetRequiredService<AppDb>();

    var stored = await db.Devices.IgnoreQueryFilters().SingleAsync(x => x.Id == device.Id, Ct);
    stored.ConnectionId = "agent-connection";
    stored.IsOnline = isOnline;

    if (grant)
    {
      db.PermissionAssignments.Add(PermissionAssignment.CreateGrant(
        PermissionPrincipalKind.User,
        user.Id,
        PermissionNames.DeviceScriptRun,
        PermissionScopeKind.Device,
        device.Id,
        tenant.Id,
        new PrincipalDescriptor(PrincipalType.User, user.Id, tenant.Id, "test")));
    }

    await db.SaveChangesAsync(Ct);
    return new Fixture(tenant.Id, user, device.Id);
  }

  private static async Task Dispatch(TestApp testApp, Mock<IAgentHubClient> agent)
  {
    var clients = new Mock<IHubClients<IAgentHubClient>>();
    clients.Setup(x => x.Client(It.IsAny<string>())).Returns(agent.Object);
    var hubContext = new Mock<IHubContext<AgentHub, IAgentHubClient>>();
    hubContext.SetupGet(x => x.Clients).Returns(clients.Object);

    using var scope = testApp.Services.CreateScope();
    var manager = new ScriptRunManager(
      scope.ServiceProvider.GetRequiredService<AppDb>(),
      hubContext.Object,
      scope.ServiceProvider.GetRequiredService<IPermissionEvaluator>(),
      scope.ServiceProvider.GetRequiredService<IResourceDescriptorFactory>(),
      scope.ServiceProvider.GetRequiredService<TimeProvider>(),
      NullLogger<ScriptRunManager>.Instance);

    await manager.DispatchQueued(Ct);
  }

  private static async Task<ScriptResult> Read(TestApp testApp, Guid resultId)
  {
    using var scope = testApp.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    return await db.ScriptResults.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == resultId, Ct);
  }

  private static async Task<(Guid ResultId, Guid RunId)> SeedQueued(
    TestApp testApp,
    Fixture fixture,
    DateTimeOffset? createdAt = null,
    Guid? scheduleId = null)
  {
    using var scope = testApp.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

    var run = new ScriptRun
    {
      Id = Guid.NewGuid(),
      Content = "echo hi",
      CreatedAt = createdAt ?? now,
      InitiatedBy = "seed",
      InitiatedByUserId = fixture.User.Id,
      ScriptScheduleId = scheduleId,
      Shell = ScriptShell.Bash,
      TenantId = fixture.TenantId,
      TimeoutSeconds = 30,
    };
    var result = new ScriptResult
    {
      Id = Guid.NewGuid(),
      DeviceId = fixture.DeviceId,
      ScriptRunId = run.Id,
      Status = ScriptResultStatus.Queued,
      TenantId = fixture.TenantId,
    };
    run.Results = [result];
    db.ScriptRuns.Add(run);
    await db.SaveChangesAsync(Ct);
    return (result.Id, run.Id);
  }

  private sealed record Fixture(Guid TenantId, AppUser User, Guid DeviceId);
}
