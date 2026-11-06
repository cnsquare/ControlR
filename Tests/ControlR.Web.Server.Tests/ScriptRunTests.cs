using System.Security.Claims;
using ControlR.Libraries.Api.Contracts.Dtos.HubDtos;
using ControlR.Libraries.Api.Contracts.Hubs.Clients;
using ControlR.Libraries.Shared.Services.Encryption;
using ControlR.Web.Server.Api.V1;
using ControlR.Web.Server.Authz.Permissions;
using ControlR.Web.Server.Data;
using ControlR.Web.Server.Data.Entities;
using ControlR.Web.Server.Extensions.Dtos.Internal;
using ControlR.Web.Server.Hubs;
using ControlR.Web.Server.Options;
using ControlR.Web.Server.Services;
using ControlR.Web.Server.Services.DeviceManagement;
using ControlR.Web.Server.Services.Scripting;
using ControlR.Web.Server.Tests.Helpers;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace ControlR.Web.Server.Tests;

public class ScriptRunTests(ITestOutputHelper testOutput)
{
  private readonly ITestOutputHelper _testOutput = testOutput;

  [Fact]
  public async Task CreateScriptRun_AllDevicesPermitted_RecordsOfflineDeviceAsFailed()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    await Grant(testApp, user, device.Id, PermissionNames.DeviceScriptRun);

    using var scope = testApp.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    var storedDevice = await db.Devices.IgnoreQueryFilters().SingleAsync(x => x.Id == device.Id, Ct);
    storedDevice.IsOnline = false;
    await db.SaveChangesAsync(Ct);
    db.ChangeTracker.Clear();

    var controller = await scope.CreateControllerWithUser<ScriptRunsController>(user);

    var response = await controller.CreateScriptRun(Request(device.Id), tenant.Id, Ct);

    var run = Assert.IsType<V1Dtos.ScriptRunDto>(Assert.IsType<OkObjectResult>(response.Result).Value);
    var result = Assert.Single(run.Results);
    Assert.Equal(device.Id, result.DeviceId);
    Assert.Equal(ScriptResultStatus.Failed, result.Status);
    Assert.Equal("The device is offline.", result.FailureReason);
    Assert.Equal(user.UserName, run.InitiatedBy);
  }

  [Fact]
  public async Task CreateScriptRun_DeviceInAnotherTenant_ReturnsNotFoundAndRunsNothing()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var otherTenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);
    var foreignDevice = await testApp.Services.CreateTestDevice(otherTenant.Id);

    using var scope = testApp.Services.CreateScope();
    var controller = await scope.CreateControllerWithUser<ScriptRunsController>(user);

    var response = await controller.CreateScriptRun(Request(foreignDevice.Id), tenant.Id, Ct);

    ActionResultAsserts.AssertHttpStatus(response.Result, StatusCodes404);
    await AssertNoRuns(testApp);
  }

  [Fact]
  public async Task CreateScriptRun_OnePermittedOneNot_ForbidsAndRunsNothing()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);
    var permitted = await testApp.Services.CreateTestDevice(tenant.Id);
    var notPermitted = await testApp.Services.CreateTestDevice(tenant.Id);
    await Grant(testApp, user, permitted.Id, PermissionNames.DeviceScriptRun);

    using var scope = testApp.Services.CreateScope();
    var controller = await scope.CreateControllerWithUser<ScriptRunsController>(user);

    var response = await controller.CreateScriptRun(Request(permitted.Id, notPermitted.Id), tenant.Id, Ct);

    Assert.IsType<ForbidResult>(response.Result);
    await AssertNoRuns(testApp);
  }

  [Fact]
  public async Task CreateScriptRun_TerminalPermissionAlone_IsForbidden()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    await Grant(testApp, user, device.Id, PermissionNames.DeviceTerminalUse);

    using var scope = testApp.Services.CreateScope();
    var controller = await scope.CreateControllerWithUser<ScriptRunsController>(user);

    var response = await controller.CreateScriptRun(Request(device.Id), tenant.Id, Ct);

    Assert.IsType<ForbidResult>(response.Result);
    await AssertNoRuns(testApp);
  }

  [Fact]
  public async Task CleanExpiredRuns_DeletesOnlyRunsPastRetention()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    var now = DateTimeOffset.UtcNow;

    var expired = await SeedRun(testApp, tenant.Id, device.Id);
    var retained = await SeedRun(testApp, tenant.Id, device.Id);
    await SetRunAge(testApp, expired, now.AddDays(-91), timeoutSeconds: 30);
    await SetRunAge(testApp, retained, now.AddDays(-89), timeoutSeconds: 30);

    var service = testApp.Services.GetRequiredService<ScriptRunCleanupBackgroundService>();
    await service.CleanExpiredRuns(Ct);

    using var scope = testApp.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    var remaining = await db.ScriptRuns.IgnoreQueryFilters().Select(x => x.Id).ToListAsync(Ct);
    Assert.Equal(retained, Assert.Single(remaining));
  }

  [Fact]
  public async Task CleanExpiredRuns_RetentionDisabled_KeepsEverything()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(
      _testOutput,
      extraConfiguration: new Dictionary<string, string?> { ["AppOptions:ScriptRunRetentionDays"] = "0" });
    var tenant = await testApp.Services.CreateTestTenant();
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    var runId = await SeedRun(testApp, tenant.Id, device.Id);
    await SetRunAge(testApp, runId, DateTimeOffset.UtcNow.AddYears(-5), timeoutSeconds: 30);

    var service = testApp.Services.GetRequiredService<ScriptRunCleanupBackgroundService>();
    var removed = await service.CleanExpiredRuns(Ct);

    Assert.Equal(0, removed);
  }

  [Fact]
  public async Task ExpireStaleResults_FailsOnlyResultsPastTimeoutAndGrace()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    var now = DateTimeOffset.UtcNow;
    var grace = ScriptResultExpiryBackgroundService.GracePeriod;

    var staleRunId = await SeedRun(testApp, tenant.Id, device.Id);
    var withinGraceRunId = await SeedRun(testApp, tenant.Id, device.Id);
    var longTimeoutRunId = await SeedRun(testApp, tenant.Id, device.Id);
    await SetRunAge(testApp, staleRunId, now - grace - TimeSpan.FromSeconds(31), timeoutSeconds: 30);
    await SetRunAge(testApp, withinGraceRunId, now - grace + TimeSpan.FromMinutes(1), timeoutSeconds: 30);
    await SetRunAge(testApp, longTimeoutRunId, now - grace - TimeSpan.FromMinutes(2), timeoutSeconds: 3600);

    var service = testApp.Services.GetRequiredService<ScriptResultExpiryBackgroundService>();
    await service.ExpireStaleResults(Ct);

    var stale = await ReadResult(testApp, await GetResultId(testApp, staleRunId));
    Assert.Equal(ScriptResultStatus.Failed, stale.Status);
    Assert.Equal(ScriptResultExpiryBackgroundService.ExpiredReason, stale.FailureReason);
    Assert.Null(stale.FinishedAt);

    var withinGrace = await ReadResult(testApp, await GetResultId(testApp, withinGraceRunId));
    Assert.Equal(ScriptResultStatus.Pending, withinGrace.Status);

    var longTimeout = await ReadResult(testApp, await GetResultId(testApp, longTimeoutRunId));
    Assert.Equal(ScriptResultStatus.Pending, longTimeout.Status);

    using var scope = testApp.Services.CreateScope();
    var report = await CreateHub(scope, device).ReportScriptResult(Report(stale.Id, 0, "late"));

    Assert.True(report.IsSuccess);
    Assert.Equal(ScriptResultStatus.Succeeded, (await ReadResult(testApp, stale.Id)).Status);
  }

  [Fact]
  public async Task GetScriptRunHistory_DeviceInAnotherTenant_ReturnsNotFound()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var otherTenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);
    var foreignDevice = await testApp.Services.CreateTestDevice(otherTenant.Id);
    await SeedRun(testApp, otherTenant.Id, foreignDevice.Id);

    using var scope = testApp.Services.CreateScope();
    var controller = await scope.CreateControllerWithUser<ScriptRunsController>(user);

    var response = await controller.GetScriptRunHistory(foreignDevice.Id, tenant.Id, cancellationToken: Ct);

    ActionResultAsserts.AssertHttpStatus(response.Result, StatusCodes404);
  }

  [Fact]
  public async Task GetScriptRunHistory_ListsOnlyThisDeviceNewestFirstAndHonorsLimit()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    var otherDevice = await testApp.Services.CreateTestDevice(tenant.Id);
    await Grant(testApp, user, device.Id, PermissionNames.DeviceScriptRun);
    var now = DateTimeOffset.UtcNow;

    var oldest = await SeedRun(testApp, tenant.Id, device.Id);
    var middle = await SeedRun(testApp, tenant.Id, device.Id);
    var newest = await SeedRun(testApp, tenant.Id, device.Id);
    var elsewhere = await SeedRun(testApp, tenant.Id, otherDevice.Id);
    await SetRunAge(testApp, oldest, now.AddHours(-3), timeoutSeconds: 30);
    await SetRunAge(testApp, middle, now.AddHours(-2), timeoutSeconds: 30);
    await SetRunAge(testApp, newest, now.AddHours(-1), timeoutSeconds: 30);
    await SetRunAge(testApp, elsewhere, now, timeoutSeconds: 30);

    using var scope = testApp.Services.CreateScope();
    var controller = await scope.CreateControllerWithUser<ScriptRunsController>(user);

    var all = await controller.GetScriptRunHistory(device.Id, tenant.Id, cancellationToken: Ct);
    var limited = await controller.GetScriptRunHistory(device.Id, tenant.Id, limit: 2, cancellationToken: Ct);

    var allItems = Assert.IsType<V1Dtos.ScriptRunHistoryItemDto[]>(Assert.IsType<OkObjectResult>(all.Result).Value);
    Assert.Equal([newest, middle, oldest], allItems.Select(x => x.ScriptRunId));

    var limitedItems = Assert.IsType<V1Dtos.ScriptRunHistoryItemDto[]>(Assert.IsType<OkObjectResult>(limited.Result).Value);
    Assert.Equal([newest, middle], limitedItems.Select(x => x.ScriptRunId));
  }

  [Fact]
  public async Task GetScriptRunHistory_LongScript_ReturnsShortPreview()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    await Grant(testApp, user, device.Id, PermissionNames.DeviceScriptRun);
    var runId = await SeedRun(testApp, tenant.Id, device.Id);

    using (var seedScope = testApp.Services.CreateScope())
    {
      var db = seedScope.ServiceProvider.GetRequiredService<AppDb>();
      var run = await db.ScriptRuns.IgnoreQueryFilters().SingleAsync(x => x.Id == runId, Ct);
      run.Content = new string('x', ScriptLimits.ContentPreviewLength * 3);
      await db.SaveChangesAsync(Ct);
    }

    using var scope = testApp.Services.CreateScope();
    var controller = await scope.CreateControllerWithUser<ScriptRunsController>(user);

    var response = await controller.GetScriptRunHistory(device.Id, tenant.Id, cancellationToken: Ct);

    var items = Assert.IsType<V1Dtos.ScriptRunHistoryItemDto[]>(Assert.IsType<OkObjectResult>(response.Result).Value);
    Assert.Equal(ScriptLimits.ContentPreviewLength, Assert.Single(items).ContentPreview.Length);
  }

  [Fact]
  public async Task GetScriptRunHistory_WithoutPermission_IsForbidden()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    await Grant(testApp, user, device.Id, PermissionNames.DeviceTerminalUse);
    await SeedRun(testApp, tenant.Id, device.Id);

    using var scope = testApp.Services.CreateScope();
    var controller = await scope.CreateControllerWithUser<ScriptRunsController>(user);

    var response = await controller.GetScriptRunHistory(device.Id, tenant.Id, cancellationToken: Ct);

    Assert.IsType<ForbidResult>(response.Result);
  }

  [Fact]
  public async Task GetScriptRun_HidesResultsForDevicesWithoutPermission()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var reader = await testApp.Services.CreateTestUser(tenant.Id);
    var visible = await testApp.Services.CreateTestDevice(tenant.Id);
    var hidden = await testApp.Services.CreateTestDevice(tenant.Id);
    await Grant(testApp, reader, visible.Id, PermissionNames.DeviceScriptRun);
    var runId = await SeedRun(testApp, tenant.Id, visible.Id, hidden.Id);

    using var scope = testApp.Services.CreateScope();
    var controller = await scope.CreateControllerWithUser<ScriptRunsController>(reader);

    var response = await controller.GetScriptRun(runId, tenant.Id, Ct);

    var run = Assert.IsType<V1Dtos.ScriptRunDto>(Assert.IsType<OkObjectResult>(response.Result).Value);
    Assert.Equal(visible.Id, Assert.Single(run.Results).DeviceId);
  }

  [Fact]
  public async Task GetScriptRun_NoPermittedDevices_ReturnsNotFound()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var reader = await testApp.Services.CreateTestUser(tenant.Id);
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    var runId = await SeedRun(testApp, tenant.Id, device.Id);

    using var scope = testApp.Services.CreateScope();
    var controller = await scope.CreateControllerWithUser<ScriptRunsController>(reader);

    var response = await controller.GetScriptRun(runId, tenant.Id, Ct);

    ActionResultAsserts.AssertHttpStatus(response.Result, StatusCodes404);
  }

  [Fact]
  public async Task ManagerStart_AgentDeclines_RecordsReasonButAcceptsLateReport()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    device.IsOnline = true;
    device.ConnectionId = "agent-connection";

    var agentClient = new Mock<IAgentHubClient>();
    agentClient
      .Setup(x => x.RunScript(It.IsAny<RunScriptRequestDto>()))
      .ReturnsAsync(HubResult.Fail("Busy."));
    var clients = new Mock<IHubClients<IAgentHubClient>>();
    clients.Setup(x => x.Client("agent-connection")).Returns(agentClient.Object);
    var hubContext = new Mock<IHubContext<AgentHub, IAgentHubClient>>();
    hubContext.SetupGet(x => x.Clients).Returns(clients.Object);

    Guid resultId;
    using (var scope = testApp.Services.CreateScope())
    {
      var manager = new ScriptRunManager(
        scope.ServiceProvider.GetRequiredService<AppDb>(),
        hubContext.Object,
        NullLogger<ScriptRunManager>.Instance);

      var run = await manager.Start(tenant.Id, "tester", [device], ScriptShell.Bash, "echo hi", 30, Ct);

      var result = Assert.Single(run.Results!);
      Assert.Equal(ScriptResultStatus.Failed, result.Status);
      Assert.Equal("Busy.", result.FailureReason);
      Assert.Null(result.FinishedAt);
      resultId = result.Id;
    }

    agentClient.Verify(
      x => x.RunScript(It.Is<RunScriptRequestDto>(r => r.ScriptResultId == resultId && r.Content == "echo hi")),
      Times.Once);

    using var hubScope = testApp.Services.CreateScope();
    var hub = CreateHub(hubScope, device);

    var report = await hub.ReportScriptResult(Report(resultId, exitCode: 0, stdout: "late"));

    Assert.True(report.IsSuccess);
    var stored = await ReadResult(testApp, resultId);
    Assert.Equal(ScriptResultStatus.Succeeded, stored.Status);
    Assert.Equal("late", stored.StandardOutput);
  }

  [Fact]
  public async Task ReportScriptResult_FromAnotherDeviceInSameTenant_IsRejected()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var target = await testApp.Services.CreateTestDevice(tenant.Id);
    var attacker = await testApp.Services.CreateTestDevice(tenant.Id);
    var runId = await SeedRun(testApp, tenant.Id, target.Id);
    var resultId = await GetResultId(testApp, runId);

    using var scope = testApp.Services.CreateScope();
    var hub = CreateHub(scope, attacker);

    var report = await hub.ReportScriptResult(Report(resultId, exitCode: 0, stdout: "forged"));

    Assert.False(report.IsSuccess);
    var stored = await ReadResult(testApp, resultId);
    Assert.Equal(ScriptResultStatus.Pending, stored.Status);
    Assert.Empty(stored.StandardOutput);
  }

  [Fact]
  public async Task ReportScriptResult_FromDeviceInAnotherTenant_IsRejected()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var otherTenant = await testApp.Services.CreateTestTenant();
    var target = await testApp.Services.CreateTestDevice(tenant.Id);
    var attacker = await testApp.Services.CreateTestDevice(otherTenant.Id);
    var runId = await SeedRun(testApp, tenant.Id, target.Id);
    var resultId = await GetResultId(testApp, runId);

    using var scope = testApp.Services.CreateScope();
    var hub = CreateHub(scope, attacker);

    var report = await hub.ReportScriptResult(Report(resultId, exitCode: 0, stdout: "forged"));

    Assert.False(report.IsSuccess);
    Assert.Equal(ScriptResultStatus.Pending, (await ReadResult(testApp, resultId)).Status);
  }

  [Fact]
  public async Task ReportScriptResult_NoRegisteredDevice_IsRejected()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var target = await testApp.Services.CreateTestDevice(tenant.Id);
    var runId = await SeedRun(testApp, tenant.Id, target.Id);
    var resultId = await GetResultId(testApp, runId);

    using var scope = testApp.Services.CreateScope();
    var hub = CreateHub(scope, device: null);

    var report = await hub.ReportScriptResult(Report(resultId, exitCode: 0, stdout: "forged"));

    Assert.False(report.IsSuccess);
    Assert.Equal(ScriptResultStatus.Pending, (await ReadResult(testApp, resultId)).Status);
  }

  [Theory]
  [InlineData(0, false, ScriptResultStatus.Succeeded)]
  [InlineData(1, false, ScriptResultStatus.Failed)]
  [InlineData(null, false, ScriptResultStatus.Failed)]
  [InlineData(null, true, ScriptResultStatus.TimedOut)]
  public async Task ReportScriptResult_OwnDevice_MapsStatus(int? exitCode, bool timedOut, ScriptResultStatus expected)
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    var runId = await SeedRun(testApp, tenant.Id, device.Id);
    var resultId = await GetResultId(testApp, runId);

    using var scope = testApp.Services.CreateScope();
    var hub = CreateHub(scope, device);

    var report = await hub.ReportScriptResult(Report(resultId, exitCode, stdout: "out", timedOut: timedOut));

    Assert.True(report.IsSuccess);
    Assert.Equal(expected, (await ReadResult(testApp, resultId)).Status);
  }

  [Fact]
  public async Task ReportScriptResult_Repeated_KeepsFirstResult()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    var runId = await SeedRun(testApp, tenant.Id, device.Id);
    var resultId = await GetResultId(testApp, runId);

    using (var scope = testApp.Services.CreateScope())
    {
      Assert.True((await CreateHub(scope, device).ReportScriptResult(Report(resultId, 0, "first"))).IsSuccess);
    }

    using (var scope = testApp.Services.CreateScope())
    {
      Assert.True((await CreateHub(scope, device).ReportScriptResult(Report(resultId, 1, "second"))).IsSuccess);
    }

    var stored = await ReadResult(testApp, resultId);
    Assert.Equal("first", stored.StandardOutput);
    Assert.Equal(0, stored.ExitCode);
  }

  [Fact]
  public async Task ReportScriptResult_StripsNulAndCapsOutput()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    var runId = await SeedRun(testApp, tenant.Id, device.Id);
    var resultId = await GetResultId(testApp, runId);
    var oversized = "a\0b" + new string('x', ScriptLimits.MaxOutputBytesPerStream * 2);

    using var scope = testApp.Services.CreateScope();
    var report = await CreateHub(scope, device).ReportScriptResult(Report(resultId, 0, oversized));

    Assert.True(report.IsSuccess);
    var stored = await ReadResult(testApp, resultId);
    Assert.StartsWith("ab", stored.StandardOutput);
    Assert.DoesNotContain('\0', stored.StandardOutput);
    Assert.Equal(ScriptLimits.MaxOutputBytesPerStream, stored.StandardOutput.Length);
  }

  [Fact]
  public async Task Postgres_CreateScriptRun_ReadsBackDatabaseGeneratedCreatedAt()
  {
    await using var testApp = await CreatePostgresApp();
    var tenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    await Grant(testApp, user, device.Id, PermissionNames.DeviceScriptRun);

    using var scope = testApp.Services.CreateScope();
    var controller = await scope.CreateControllerWithUser<ScriptRunsController>(user);

    var response = await controller.CreateScriptRun(Request(device.Id), tenant.Id, Ct);

    var run = Assert.IsType<V1Dtos.ScriptRunDto>(Assert.IsType<OkObjectResult>(response.Result).Value);
    Assert.NotEqual(default, run.CreatedAt);
    Assert.Equal(device.Id, Assert.Single(run.Results).DeviceId);

    var read = await controller.GetScriptRun(run.Id, tenant.Id, Ct);
    Assert.IsType<OkObjectResult>(read.Result);
  }

  [Fact]
  public async Task Postgres_DeletingDevice_CascadesToItsResults()
  {
    await using var testApp = await CreatePostgresApp();
    var tenant = await testApp.Services.CreateTestTenant();
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    var runId = await SeedRun(testApp, tenant.Id, device.Id);

    using var scope = testApp.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    await db.Devices.IgnoreQueryFilters().Where(x => x.Id == device.Id).ExecuteDeleteAsync(Ct);

    Assert.False(await db.ScriptResults.IgnoreQueryFilters().AnyAsync(x => x.ScriptRunId == runId, Ct));
    Assert.True(await db.ScriptRuns.IgnoreQueryFilters().AnyAsync(x => x.Id == runId, Ct));
  }

  [Fact]
  public async Task Postgres_ExpireStaleResults_TranslatesAndPersists()
  {
    await using var testApp = await CreatePostgresApp();
    var tenant = await testApp.Services.CreateTestTenant();
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    var now = testApp.Services.GetRequiredService<TimeProvider>().GetUtcNow();
    var grace = ScriptResultExpiryBackgroundService.GracePeriod;

    var stale = await SeedRun(testApp, tenant.Id, device.Id);
    var fresh = await SeedRun(testApp, tenant.Id, device.Id);
    await SetRunAge(testApp, stale, now - grace - TimeSpan.FromMinutes(2), timeoutSeconds: 30);
    await SetRunAge(testApp, fresh, now, timeoutSeconds: 30);

    var service = testApp.Services.GetRequiredService<ScriptResultExpiryBackgroundService>();
    var expired = await service.ExpireStaleResults(Ct);

    Assert.Equal(1, expired);
    Assert.Equal(ScriptResultStatus.Failed, (await ReadResult(testApp, await GetResultId(testApp, stale))).Status);
    Assert.Equal(ScriptResultStatus.Pending, (await ReadResult(testApp, await GetResultId(testApp, fresh))).Status);
  }

  [Fact]
  public async Task Postgres_GetScriptRunHistory_TranslatesPreviewAndOrdering()
  {
    await using var testApp = await CreatePostgresApp();
    var tenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    await Grant(testApp, user, device.Id, PermissionNames.DeviceScriptRun);
    var now = testApp.Services.GetRequiredService<TimeProvider>().GetUtcNow();

    var older = await SeedRun(testApp, tenant.Id, device.Id);
    var newer = await SeedRun(testApp, tenant.Id, device.Id);
    await SetRunAge(testApp, older, now.AddHours(-2), timeoutSeconds: 30);
    await SetRunAge(testApp, newer, now.AddHours(-1), timeoutSeconds: 30);

    using (var seedScope = testApp.Services.CreateScope())
    {
      var db = seedScope.ServiceProvider.GetRequiredService<AppDb>();
      var run = await db.ScriptRuns.IgnoreQueryFilters().SingleAsync(x => x.Id == newer, Ct);
      run.Content = new string('x', ScriptLimits.ContentPreviewLength * 3);
      await db.SaveChangesAsync(Ct);
    }

    using var scope = testApp.Services.CreateScope();
    var controller = await scope.CreateControllerWithUser<ScriptRunsController>(user);

    var response = await controller.GetScriptRunHistory(device.Id, tenant.Id, cancellationToken: Ct);

    var items = Assert.IsType<V1Dtos.ScriptRunHistoryItemDto[]>(Assert.IsType<OkObjectResult>(response.Result).Value);
    Assert.Equal([newer, older], items.Select(x => x.ScriptRunId));
    Assert.Equal(ScriptLimits.ContentPreviewLength, items[0].ContentPreview.Length);
    Assert.Equal("echo hi", items[1].ContentPreview);
  }

  [Fact]
  public async Task Postgres_Model_HasNoPendingMigrationChanges()
  {
    await using var testApp = await CreatePostgresApp();

    using var scope = testApp.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();

    Assert.False(db.Database.HasPendingModelChanges());
    Assert.Contains(await db.Database.GetAppliedMigrationsAsync(Ct), x => x.EndsWith("_AddScriptRuns"));
  }

  [Fact]
  public async Task Postgres_ReportScriptResult_StoresBinaryOutput()
  {
    await using var testApp = await CreatePostgresApp();
    var tenant = await testApp.Services.CreateTestTenant();
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    var runId = await SeedRun(testApp, tenant.Id, device.Id);
    var resultId = await GetResultId(testApp, runId);

    using var scope = testApp.Services.CreateScope();
    var report = await CreateHub(scope, device).ReportScriptResult(Report(resultId, 0, "bin\0ary"));

    Assert.True(report.IsSuccess);
    Assert.Equal("binary", (await ReadResult(testApp, resultId)).StandardOutput);
  }

  [Fact]
  public async Task Postgres_RetentionDelete_CascadesToResults()
  {
    await using var testApp = await CreatePostgresApp();
    var tenant = await testApp.Services.CreateTestTenant();
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    var now = testApp.Services.GetRequiredService<TimeProvider>().GetUtcNow();

    var expired = await SeedRun(testApp, tenant.Id, device.Id);
    var retained = await SeedRun(testApp, tenant.Id, device.Id);
    await SetRunAge(testApp, expired, now.AddDays(-91), timeoutSeconds: 30);
    await SetRunAge(testApp, retained, now.AddDays(-1), timeoutSeconds: 30);

    var service = testApp.Services.GetRequiredService<ScriptRunCleanupBackgroundService>();
    var removed = await service.CleanExpiredRuns(Ct);

    Assert.Equal(1, removed);

    using var scope = testApp.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    Assert.False(await db.ScriptResults.IgnoreQueryFilters().AnyAsync(x => x.ScriptRunId == expired, Ct));
    Assert.True(await db.ScriptResults.IgnoreQueryFilters().AnyAsync(x => x.ScriptRunId == retained, Ct));
  }

  private const int StatusCodes404 = 404;

  private static CancellationToken Ct => TestContext.Current.CancellationToken;

  private static async Task AssertNoRuns(TestApp testApp)
  {
    using var scope = testApp.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    Assert.False(await db.ScriptRuns.IgnoreQueryFilters().AnyAsync(Ct));
  }

  private static AgentHub CreateHub(IServiceScope scope, Device? device)
  {
    var services = scope.ServiceProvider;
    var hub = new AgentHub(
      services.GetRequiredService<AppDb>(),
      services.GetRequiredService<TimeProvider>(),
      new Mock<IHubContext<ViewerHub, IViewerHubClient>>().Object,
      services.GetRequiredService<IDeviceManager>(),
      new Mock<IHubStreamStore>().Object,
      new Mock<IAgentVersionProvider>().Object,
      services.GetRequiredService<IOptions<AppOptions>>(),
      Microsoft.Extensions.Options.Options.Create(new DeveloperOptions()),
      services.GetRequiredService<IOptions<ServerLifecycleOptions>>(),
      new Mock<IEd25519KeyProvider>().Object,
      services.GetRequiredService<ILogger<AgentHub>>());

    var context = new TestHubCallerContext();
    if (device is not null)
    {
      context.Items["Device"] = device.ToInternalResponseDto(isOutdated: false);
    }

    hub.Context = context;
    return hub;
  }

  private async Task<TestApp> CreatePostgresApp()
  {
    return await TestAppBuilder.CreateTestApp(
      _testOutput,
      testDatabaseName: $"{Guid.NewGuid()}",
      useInMemoryDatabase: false);
  }

  private static async Task<Guid> GetResultId(TestApp testApp, Guid runId)
  {
    using var scope = testApp.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    return await db.ScriptResults
      .IgnoreQueryFilters()
      .Where(x => x.ScriptRunId == runId)
      .Select(x => x.Id)
      .SingleAsync(Ct);
  }

  private static async Task Grant(TestApp testApp, AppUser user, Guid deviceId, string permissionName)
  {
    using var scope = testApp.Services.CreateScope();
    await using var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    db.PermissionAssignments.Add(PermissionAssignment.CreateGrant(
      PermissionPrincipalKind.User,
      user.Id,
      permissionName,
      PermissionScopeKind.Device,
      deviceId,
      user.TenantId,
      new PrincipalDescriptor(PrincipalType.User, user.Id, user.TenantId, "test")));
    await db.SaveChangesAsync(Ct);
  }

  private static async Task<ScriptResult> ReadResult(TestApp testApp, Guid resultId)
  {
    using var scope = testApp.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    return await db.ScriptResults.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == resultId, Ct);
  }

  private static ScriptExecutionResultDto Report(Guid resultId, int? exitCode, string stdout, bool timedOut = false)
  {
    return new ScriptExecutionResultDto(
      resultId,
      exitCode,
      stdout,
      string.Empty,
      OutputTruncated: false,
      TimedOut: timedOut,
      FailureReason: null,
      StartedAt: DateTimeOffset.UtcNow,
      FinishedAt: DateTimeOffset.UtcNow);
  }

  private static V1Dtos.CreateScriptRunRequestDto Request(params Guid[] deviceIds)
  {
    return new V1Dtos.CreateScriptRunRequestDto(deviceIds, ScriptShell.Bash, "echo hi");
  }

  private static async Task<Guid> SeedRun(TestApp testApp, Guid tenantId, params Guid[] deviceIds)
  {
    using var scope = testApp.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    var run = new ScriptRun
    {
      Id = Guid.NewGuid(),
      Content = "echo hi",
      InitiatedBy = "seed",
      Shell = ScriptShell.Bash,
      TenantId = tenantId,
      TimeoutSeconds = 30,
    };
    run.Results =
    [
      .. deviceIds.Select(deviceId => new ScriptResult
      {
        Id = Guid.NewGuid(),
        DeviceId = deviceId,
        ScriptRunId = run.Id,
        TenantId = tenantId,
      })
    ];
    db.ScriptRuns.Add(run);
    await db.SaveChangesAsync(Ct);
    return run.Id;
  }

  private static async Task SetRunAge(TestApp testApp, Guid runId, DateTimeOffset createdAt, int timeoutSeconds)
  {
    using var scope = testApp.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    var run = await db.ScriptRuns.IgnoreQueryFilters().SingleAsync(x => x.Id == runId, Ct);
    run.CreatedAt = createdAt;
    run.TimeoutSeconds = timeoutSeconds;
    await db.SaveChangesAsync(Ct);
  }

  private sealed class TestHubCallerContext : HubCallerContext
  {
    public override CancellationToken ConnectionAborted => CancellationToken.None;
    public override string ConnectionId { get; } = Guid.NewGuid().ToString();
    public override IFeatureCollection Features { get; } = new FeatureCollection();
    public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
    public override ClaimsPrincipal User { get; } = new();
    public override string? UserIdentifier => null;

    public override void Abort()
    {
    }
  }
}
