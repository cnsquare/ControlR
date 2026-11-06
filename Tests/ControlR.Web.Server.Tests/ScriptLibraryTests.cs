using System.Reflection;
using ControlR.Web.Server.Api.V1;
using ControlR.Web.Server.Authz.Permissions;
using ControlR.Web.Server.Data;
using ControlR.Web.Server.Data.Entities;
using ControlR.Web.Server.Services.Scripting;
using ControlR.Web.Server.Tests.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ControlR.Web.Server.Tests;

public class ScriptLibraryTests(ITestOutputHelper testOutput)
{
  private readonly ITestOutputHelper _testOutput = testOutput;

  private static CancellationToken Ct => TestContext.Current.CancellationToken;

  [Theory]
  [InlineData(nameof(SavedScriptsController.CreateSavedScript), typeof(SavedScriptsController), PolicyNames.RequireScriptsWrite)]
  [InlineData(nameof(SavedScriptsController.DeleteSavedScript), typeof(SavedScriptsController), PolicyNames.RequireScriptsWrite)]
  [InlineData(nameof(SavedScriptsController.GetSavedScripts), typeof(SavedScriptsController), PolicyNames.RequireScriptsRead)]
  [InlineData(nameof(SavedScriptsController.UpdateSavedScript), typeof(SavedScriptsController), PolicyNames.RequireScriptsWrite)]
  [InlineData(nameof(ScriptSchedulesController.CreateScriptSchedule), typeof(ScriptSchedulesController), PolicyNames.RequireScriptsWrite)]
  [InlineData(nameof(ScriptSchedulesController.DeleteScriptSchedule), typeof(ScriptSchedulesController), PolicyNames.RequireScriptsWrite)]
  [InlineData(nameof(ScriptSchedulesController.GetScriptSchedules), typeof(ScriptSchedulesController), PolicyNames.RequireScriptsRead)]
  [InlineData(nameof(ScriptSchedulesController.SetScriptScheduleEnabled), typeof(ScriptSchedulesController), PolicyNames.RequireScriptsWrite)]
  public void Actions_RequireTheLibraryPolicy(string actionName, Type controller, string expectedPolicy)
  {
    var action = controller.GetMethod(actionName);

    Assert.NotNull(action);
    Assert.Equal(expectedPolicy, action.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
  }

  [Fact]
  public async Task CreateSavedScript_DuplicateName_IsAConflict()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);
    using var scope = testApp.Services.CreateScope();
    var controller = await scope.CreateControllerWithUser<SavedScriptsController>(user);

    await controller.CreateSavedScript(ScriptRequest("Cleanup"), tenant.Id, Ct);
    var second = await controller.CreateSavedScript(ScriptRequest("Cleanup"), tenant.Id, Ct);

    ActionResultAsserts.AssertHttpStatus(second.Result, 409);
  }

  [Fact]
  public async Task CreateScriptSchedule_DeviceWithoutRunPermission_IsForbidden()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);
    var permitted = await testApp.Services.CreateTestDevice(tenant.Id);
    var other = await testApp.Services.CreateTestDevice(tenant.Id);
    await Grant(testApp, user, permitted.Id);
    var script = await SeedScript(testApp, tenant.Id);

    using var scope = testApp.Services.CreateScope();
    var controller = await scope.CreateControllerWithUser<ScriptSchedulesController>(user);

    var response = await controller.CreateScriptSchedule(
      ScheduleRequest(script.Id, permitted.Id, other.Id), tenant.Id, Ct);

    Assert.IsType<ForbidResult>(response.Result);
    await AssertNoSchedules(testApp);
  }

  [Fact]
  public async Task CreateScriptSchedule_ScriptInAnotherTenant_IsNotFound()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var otherTenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    await Grant(testApp, user, device.Id);
    var foreignScript = await SeedScript(testApp, otherTenant.Id);

    using var scope = testApp.Services.CreateScope();
    var controller = await scope.CreateControllerWithUser<ScriptSchedulesController>(user);

    var response = await controller.CreateScriptSchedule(ScheduleRequest(foreignScript.Id, device.Id), tenant.Id, Ct);

    ActionResultAsserts.AssertHttpStatus(response.Result, 404);
    await AssertNoSchedules(testApp);
  }

  [Theory]
  [InlineData(ScriptScheduleInterval.Hourly, 1)]
  [InlineData(ScriptScheduleInterval.Daily, 24)]
  [InlineData(ScriptScheduleInterval.Weekly, 168)]
  public void GetNextRun_AdvancesOneInterval(ScriptScheduleInterval interval, int expectedHours)
  {
    var start = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    var next = ScriptScheduleBackgroundService.GetNextRun(start, interval, start);

    Assert.Equal(start.AddHours(expectedHours), next);
  }

  [Fact]
  public void GetNextRun_AfterAnOutage_SkipsMissedRunsAndStaysOnTheSameTimeOfDay()
  {
    var start = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
    var now = start.AddDays(10).AddHours(3);

    var next = ScriptScheduleBackgroundService.GetNextRun(start, ScriptScheduleInterval.Daily, now);

    Assert.Equal(start.AddDays(11), next);
  }

  [Fact]
  public async Task GetSavedScripts_OnlyReturnsThisTenant()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var otherTenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);
    await SeedScript(testApp, tenant.Id, "mine");
    await SeedScript(testApp, otherTenant.Id, "theirs");

    using var scope = testApp.Services.CreateScope();
    var controller = await scope.CreateControllerWithUser<SavedScriptsController>(user);

    var response = await controller.GetSavedScripts(tenant.Id, Ct);

    var scripts = Assert.IsType<V1Dtos.SavedScriptDto[]>(Assert.IsType<OkObjectResult>(response.Result).Value);
    Assert.Equal("mine", Assert.Single(scripts).Name);
  }

  [Fact]
  public async Task Postgres_RunDueSchedules_TranslatesDeviceListAndRuns()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(
      _testOutput,
      testDatabaseName: $"{Guid.NewGuid()}",
      useInMemoryDatabase: false);
    var tenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    await Grant(testApp, user, device.Id);
    var scheduleId = await SeedSchedule(testApp, tenant.Id, user.Id, [device.Id]);

    await RunDue(testApp);

    var schedule = await ReadSchedule(testApp, scheduleId);
    Assert.NotNull(schedule.LastRunId);
    Assert.Equal([device.Id], schedule.DeviceIds);
    Assert.Equal("echo copy", await ReadRunContent(testApp, schedule.LastRunId.Value));
  }

  [Fact]
  public async Task RunDueSchedules_CreatorLostPermission_RunsNothing()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    var scheduleId = await SeedSchedule(testApp, tenant.Id, user.Id, [device.Id]);

    await RunDue(testApp);

    var schedule = await ReadSchedule(testApp, scheduleId);
    Assert.Null(schedule.LastRunId);
    Assert.Contains("can no longer run", schedule.LastError);
    await AssertNoRuns(testApp);
  }

  [Fact]
  public async Task RunDueSchedules_CreatorLostSomeDevices_RunsOnlyTheRest()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);
    var kept = await testApp.Services.CreateTestDevice(tenant.Id);
    var lost = await testApp.Services.CreateTestDevice(tenant.Id);
    await Grant(testApp, user, kept.Id);
    var scheduleId = await SeedSchedule(testApp, tenant.Id, user.Id, [kept.Id, lost.Id]);

    await RunDue(testApp);

    var schedule = await ReadSchedule(testApp, scheduleId);
    Assert.Contains("Skipped 1", schedule.LastError);

    using var scope = testApp.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    var result = await db.ScriptResults.IgnoreQueryFilters().SingleAsync(Ct);
    Assert.Equal(kept.Id, result.DeviceId);
  }

  [Fact]
  public async Task RunDueSchedules_CreatorMovedToAnotherTenant_DisablesTheSchedule()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var otherTenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(otherTenant.Id);
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    var scheduleId = await SeedSchedule(testApp, tenant.Id, user.Id, [device.Id]);

    await RunDue(testApp);

    var schedule = await ReadSchedule(testApp, scheduleId);
    Assert.False(schedule.IsEnabled);
    Assert.Contains("creator", schedule.LastError);
    await AssertNoRuns(testApp);
  }

  [Fact]
  public async Task RunDueSchedules_DeviceInAnotherTenant_IsNeverRun()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var otherTenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);
    var foreignDevice = await testApp.Services.CreateTestDevice(otherTenant.Id);
    await SeedSchedule(testApp, tenant.Id, user.Id, [foreignDevice.Id]);

    await RunDue(testApp);

    await AssertNoRuns(testApp);
  }

  [Fact]
  public async Task RunDueSchedules_Disabled_DoesNotRun()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    await Grant(testApp, user, device.Id);
    await SeedSchedule(testApp, tenant.Id, user.Id, [device.Id], isEnabled: false);

    await RunDue(testApp);

    await AssertNoRuns(testApp);
  }

  [Fact]
  public async Task RunDueSchedules_Due_RunsOnceAndMovesToTheNextSlot()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    await Grant(testApp, user, device.Id);
    var scheduleId = await SeedSchedule(testApp, tenant.Id, user.Id, [device.Id]);

    await RunDue(testApp);
    await RunDue(testApp);

    var schedule = await ReadSchedule(testApp, scheduleId);
    var now = testApp.Services.GetRequiredService<TimeProvider>().GetUtcNow();
    Assert.True(schedule.NextRun > now);

    using var scope = testApp.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    Assert.Single(await db.ScriptRuns.IgnoreQueryFilters().ToListAsync(Ct));
  }

  [Fact]
  public async Task RunDueSchedules_NotDue_DoesNotRun()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    await Grant(testApp, user, device.Id);
    var now = testApp.Services.GetRequiredService<TimeProvider>().GetUtcNow();
    await SeedSchedule(testApp, tenant.Id, user.Id, [device.Id], nextRun: now.AddHours(1));

    await RunDue(testApp);

    await AssertNoRuns(testApp);
  }

  [Fact]
  public async Task Schedule_KeepsItsCopyWhenTheLibraryScriptIsEdited()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    await Grant(testApp, user, device.Id);
    var script = await SeedScript(testApp, tenant.Id);

    Guid scheduleId;
    using (var scope = testApp.Services.CreateScope())
    {
      var schedules = await scope.CreateControllerWithUser<ScriptSchedulesController>(user);
      var created = await schedules.CreateScriptSchedule(ScheduleRequest(script.Id, device.Id), tenant.Id, Ct);
      scheduleId = Assert.IsType<V1Dtos.ScriptScheduleDto>(Assert.IsType<OkObjectResult>(created.Result).Value).Id;

      var scripts = await scope.CreateControllerWithUser<SavedScriptsController>(user);
      await scripts.UpdateSavedScript(script.Id, ScriptRequest(script.Name, "echo tampered"), tenant.Id, Ct);
    }

    await RunDue(testApp);

    var schedule = await ReadSchedule(testApp, scheduleId);
    Assert.NotNull(schedule.LastRunId);
    Assert.Equal("echo original", await ReadRunContent(testApp, schedule.LastRunId.Value));
  }

  [Fact]
  public async Task SetScriptScheduleEnabled_ResumingWithoutDeviceAccess_IsForbiddenButPausingIsNot()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    var creator = await testApp.Services.CreateTestUser(tenant.Id, email: $"c-{Guid.NewGuid():N}@t.local");
    var other = await testApp.Services.CreateTestUser(tenant.Id, email: $"o-{Guid.NewGuid():N}@t.local");
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    var scheduleId = await SeedSchedule(testApp, tenant.Id, creator.Id, [device.Id]);

    using var scope = testApp.Services.CreateScope();
    var controller = await scope.CreateControllerWithUser<ScriptSchedulesController>(other);

    var paused = await controller.SetScriptScheduleEnabled(scheduleId, new(false), tenant.Id, Ct);
    var resumed = await controller.SetScriptScheduleEnabled(scheduleId, new(true), tenant.Id, Ct);

    Assert.IsType<OkObjectResult>(paused.Result);
    Assert.IsType<ForbidResult>(resumed.Result);
    Assert.False((await ReadSchedule(testApp, scheduleId)).IsEnabled);
  }

  private static async Task AssertNoRuns(TestApp testApp)
  {
    using var scope = testApp.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    Assert.False(await db.ScriptRuns.IgnoreQueryFilters().AnyAsync(Ct));
  }

  private static async Task AssertNoSchedules(TestApp testApp)
  {
    using var scope = testApp.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    Assert.False(await db.ScriptSchedules.IgnoreQueryFilters().AnyAsync(Ct));
  }

  private static async Task Grant(TestApp testApp, AppUser user, Guid deviceId)
  {
    using var scope = testApp.Services.CreateScope();
    await using var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    db.PermissionAssignments.Add(PermissionAssignment.CreateGrant(
      PermissionPrincipalKind.User,
      user.Id,
      PermissionNames.DeviceScriptRun,
      PermissionScopeKind.Device,
      deviceId,
      user.TenantId,
      new PrincipalDescriptor(PrincipalType.User, user.Id, user.TenantId, "test")));
    await db.SaveChangesAsync(Ct);
  }

  private static async Task<string> ReadRunContent(TestApp testApp, Guid runId)
  {
    using var scope = testApp.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    return (await db.ScriptRuns.IgnoreQueryFilters().SingleAsync(x => x.Id == runId, Ct)).Content;
  }

  private static async Task<ScriptSchedule> ReadSchedule(TestApp testApp, Guid scheduleId)
  {
    using var scope = testApp.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    return await db.ScriptSchedules.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == scheduleId, Ct);
  }

  private static Task RunDue(TestApp testApp)
  {
    return testApp.Services.GetRequiredService<ScriptScheduleBackgroundService>().RunDueSchedules(Ct);
  }

  private static V1Dtos.CreateScriptScheduleRequestDto ScheduleRequest(Guid scriptId, params Guid[] deviceIds)
  {
    return new V1Dtos.CreateScriptScheduleRequestDto("Nightly", scriptId, deviceIds, ScriptScheduleInterval.Daily);
  }

  private static V1Dtos.SaveSavedScriptRequestDto ScriptRequest(string name, string content = "echo hi")
  {
    return new V1Dtos.SaveSavedScriptRequestDto(name, ScriptShell.Bash, content);
  }

  private static async Task<SavedScript> SeedScript(TestApp testApp, Guid tenantId, string name = "Original")
  {
    using var scope = testApp.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    var script = new SavedScript
    {
      Id = Guid.NewGuid(),
      Content = "echo original",
      CreatedBy = "seed",
      Name = name,
      Shell = ScriptShell.Bash,
      TenantId = tenantId,
      TimeoutSeconds = 30,
    };
    db.SavedScripts.Add(script);
    await db.SaveChangesAsync(Ct);
    return script;
  }

  private static async Task<Guid> SeedSchedule(
    TestApp testApp,
    Guid tenantId,
    Guid creatorId,
    List<Guid> deviceIds,
    bool isEnabled = true,
    DateTimeOffset? nextRun = null)
  {
    using var scope = testApp.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
    var schedule = new ScriptSchedule
    {
      Id = Guid.NewGuid(),
      Content = "echo copy",
      CreatedBy = "seed",
      CreatedByUserId = creatorId,
      DeviceIds = deviceIds,
      Interval = ScriptScheduleInterval.Daily,
      IsEnabled = isEnabled,
      Name = $"schedule-{Guid.NewGuid():N}",
      NextRun = nextRun ?? now.AddMinutes(-1),
      ScriptName = "copy",
      Shell = ScriptShell.Bash,
      TenantId = tenantId,
      TimeoutSeconds = 30,
    };
    db.ScriptSchedules.Add(schedule);
    await db.SaveChangesAsync(Ct);
    return schedule.Id;
  }
}
