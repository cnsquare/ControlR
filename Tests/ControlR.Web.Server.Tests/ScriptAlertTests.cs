using ControlR.Web.Server.Api.V1;
using ControlR.Web.Server.Data;
using ControlR.Web.Server.Data.Entities;
using ControlR.Web.Server.Primitives;
using ControlR.Web.Server.Services;
using ControlR.Web.Server.Services.Scripting;
using ControlR.Web.Server.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ControlR.Web.Server.Tests;

public class ScriptAlertTests(ITestOutputHelper testOutput)
{
  private const string Secret = "hunter2-do-not-email";

  private readonly ITestOutputHelper _testOutput = testOutput;

  private static CancellationToken Ct => TestContext.Current.CancellationToken;

  [Fact]
  public async Task DismissScriptAlert_InAnotherTenant_IsNotFound()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var fixture = await CreateFixture(testApp);
    var otherTenant = await testApp.Services.CreateTestTenant();
    var outsider = await testApp.Services.CreateTestUser(otherTenant.Id, email: $"o-{Guid.NewGuid():N}@t.local");
    await SeedResult(testApp, fixture, ScriptResultStatus.Failed);
    await RaiseAlerts(testApp);
    var alert = await SingleAlert(testApp);

    using var scope = testApp.Services.CreateScope();
    var controller = await scope.CreateControllerWithUser<ScriptAlertsController>(outsider);

    var response = await controller.DismissScriptAlert(alert.Id, otherTenant.Id, Ct);

    ActionResultAsserts.AssertHttpStatus(response, 404);
    Assert.Null((await SingleAlert(testApp)).DismissedAt);
  }

  [Fact]
  public async Task Email_LeavesOutScriptOutputAndEncodesNames()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var fixture = await CreateFixture(testApp, deviceName: "<img src=x onerror=alert(1)>");
    await SeedResult(testApp, fixture, ScriptResultStatus.Failed);
    var email = EmailMock();

    await RaiseAlerts(testApp, email);

    email.Verify(x => x.SendEmailWithResult(
      "ops@example.com",
      It.IsAny<string>(),
      It.Is<string>(body =>
        !body.Contains(Secret) &&
        !body.Contains("<img") &&
        body.Contains("&lt;img"))), Times.Once);
  }

  [Fact]
  public async Task FailedScheduledResult_RaisesAnAlert()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var fixture = await CreateFixture(testApp);
    await SeedResult(testApp, fixture, ScriptResultStatus.Failed);

    await RaiseAlerts(testApp);

    var alert = await SingleAlert(testApp);
    Assert.Equal(fixture.ScheduleId, alert.ScheduleId);
    Assert.Equal(fixture.DeviceId, alert.DeviceId);
    Assert.Contains("exit code 3", alert.Message);
  }

  [Fact]
  public async Task GetScriptAlerts_HidesDismissedAndOtherTenants()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var fixture = await CreateFixture(testApp);
    var other = await CreateFixture(testApp);
    await SeedResult(testApp, fixture, ScriptResultStatus.Failed);
    await SeedResult(testApp, other, ScriptResultStatus.Failed);
    await RaiseAlerts(testApp);

    using var scope = testApp.Services.CreateScope();
    var controller = await scope.CreateControllerWithUser<ScriptAlertsController>(fixture.User);

    var response = await controller.GetScriptAlerts(fixture.TenantId, Ct);
    var alerts = Assert.IsType<V1Dtos.ScriptAlertDto[]>(Assert.IsType<OkObjectResult>(response.Result).Value);
    var alert = Assert.Single(alerts);
    Assert.Equal(fixture.ScheduleId, alert.ScheduleId);

    await controller.DismissScriptAlert(alert.Id, fixture.TenantId, Ct);

    var after = await controller.GetScriptAlerts(fixture.TenantId, Ct);
    Assert.Empty(Assert.IsType<V1Dtos.ScriptAlertDto[]>(Assert.IsType<OkObjectResult>(after.Result).Value));
  }

  [Fact]
  public async Task Postgres_RaisesAndListsAlerts()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(
      _testOutput,
      testDatabaseName: $"{Guid.NewGuid()}",
      useInMemoryDatabase: false);
    var fixture = await CreateFixture(testApp);
    await SeedResult(testApp, fixture, ScriptResultStatus.TimedOut);

    await RaiseAlerts(testApp);

    Assert.Contains("timed out", (await SingleAlert(testApp)).Message);
  }

  [Fact]
  public async Task RepeatFailures_UpdateOneAlertAndEmailOnce()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var fixture = await CreateFixture(testApp);
    var email = EmailMock();

    for (var i = 0; i < 3; i++)
    {
      await SeedResult(testApp, fixture, ScriptResultStatus.Failed);
      await RaiseAlerts(testApp, email);
    }

    Assert.Equal(3, (await SingleAlert(testApp)).Occurrences);
    email.Verify(x => x.SendEmailWithResult(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
  }

  [Fact]
  public async Task AfterDismissing_TheNextFailureRaisesAndEmailsAgain()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var fixture = await CreateFixture(testApp);
    var email = EmailMock();
    await SeedResult(testApp, fixture, ScriptResultStatus.Failed);
    await RaiseAlerts(testApp, email);

    using (var scope = testApp.Services.CreateScope())
    {
      var controller = await scope.CreateControllerWithUser<ScriptAlertsController>(fixture.User);
      await controller.DismissScriptAlert((await SingleAlert(testApp)).Id, fixture.TenantId, Ct);
    }

    await SeedResult(testApp, fixture, ScriptResultStatus.Failed);
    await RaiseAlerts(testApp, email);

    email.Verify(x => x.SendEmailWithResult(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(2));
  }

  [Fact]
  public async Task ScheduleLostAccess_RaisesAnAlertFromTheSchedulerItself()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var fixture = await CreateFixture(testApp, nextRun: DateTimeOffset.MinValue.AddYears(2000));

    await testApp.Services.GetRequiredService<ScriptScheduleBackgroundService>().RunDueSchedules(Ct);

    var alert = await SingleAlert(testApp);
    Assert.Null(alert.DeviceId);
    Assert.Contains("can no longer run", alert.Message);
  }

  [Theory]
  [InlineData(ScriptResultStatus.Pending)]
  [InlineData(ScriptResultStatus.Queued)]
  [InlineData(ScriptResultStatus.Succeeded)]
  public async Task UnfinishedOrSuccessfulResults_RaiseNothing(ScriptResultStatus status)
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var fixture = await CreateFixture(testApp);
    await SeedResult(testApp, fixture, status);

    await RaiseAlerts(testApp);

    await AssertNoAlerts(testApp);
  }

  [Fact]
  public async Task OneOffRuns_RaiseNothing()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var fixture = await CreateFixture(testApp);
    await SeedResult(testApp, fixture, ScriptResultStatus.Failed, scheduled: false);

    await RaiseAlerts(testApp);

    await AssertNoAlerts(testApp);
  }

  [Fact]
  public async Task PausedSchedule_RaisesNothing()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var fixture = await CreateFixture(testApp, isEnabled: false);
    await SeedResult(testApp, fixture, ScriptResultStatus.Failed);

    await RaiseAlerts(testApp);

    await AssertNoAlerts(testApp);
  }

  private static async Task AssertNoAlerts(TestApp testApp)
  {
    using var scope = testApp.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    Assert.False(await db.ScriptAlerts.IgnoreQueryFilters().AnyAsync(Ct));
  }

  private static async Task<Fixture> CreateFixture(
    TestApp testApp,
    bool isEnabled = true,
    string? deviceName = null,
    DateTimeOffset? nextRun = null)
  {
    var tenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id, email: $"u-{Guid.NewGuid():N}@t.local");
    var device = await testApp.Services.CreateTestDevice(tenant.Id);

    using var scope = testApp.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

    if (deviceName is not null)
    {
      var stored = await db.Devices.IgnoreQueryFilters().SingleAsync(x => x.Id == device.Id, Ct);
      stored.Name = deviceName;
    }

    var schedule = new ScriptSchedule
    {
      Id = Guid.NewGuid(),
      AlertEmail = "ops@example.com",
      Content = "echo hi",
      CreatedBy = "seed",
      CreatedByUserId = user.Id,
      DeviceIds = [device.Id],
      Interval = ScriptScheduleInterval.Hourly,
      IsEnabled = isEnabled,
      Name = "Nightly",
      NextRun = nextRun ?? now.AddHours(1),
      ScriptName = "copy",
      Shell = ScriptShell.Bash,
      TenantId = tenant.Id,
      TimeoutSeconds = 30,
    };
    db.ScriptSchedules.Add(schedule);
    await db.SaveChangesAsync(Ct);
    return new Fixture(tenant.Id, user, device.Id, schedule.Id);
  }

  private static Mock<IControlrEmailSender> EmailMock()
  {
    var email = new Mock<IControlrEmailSender>();
    email
      .Setup(x => x.SendEmailWithResult(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
      .ReturnsAsync(HttpResult.Ok());
    return email;
  }

  private static async Task RaiseAlerts(TestApp testApp, Mock<IControlrEmailSender>? email = null)
  {
    using var scope = testApp.Services.CreateScope();
    var manager = new ScriptAlertManager(
      scope.ServiceProvider.GetRequiredService<AppDb>(),
      (email ?? EmailMock()).Object,
      scope.ServiceProvider.GetRequiredService<TimeProvider>(),
      NullLogger<ScriptAlertManager>.Instance);

    await manager.RaiseForFailedResults(Ct);
  }

  private static async Task SeedResult(
    TestApp testApp,
    Fixture fixture,
    ScriptResultStatus status,
    bool scheduled = true)
  {
    using var scope = testApp.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

    var run = new ScriptRun
    {
      Id = Guid.NewGuid(),
      Content = "echo hi",
      CreatedAt = now,
      InitiatedBy = "seed",
      ScriptScheduleId = scheduled ? fixture.ScheduleId : null,
      Shell = ScriptShell.Bash,
      TenantId = fixture.TenantId,
      TimeoutSeconds = 30,
    };
    run.Results =
    [
      new ScriptResult
      {
        Id = Guid.NewGuid(),
        DeviceId = fixture.DeviceId,
        ExitCode = 3,
        ScriptRunId = run.Id,
        StandardError = Secret,
        StandardOutput = Secret,
        Status = status,
        TenantId = fixture.TenantId,
      }
    ];
    db.ScriptRuns.Add(run);
    await db.SaveChangesAsync(Ct);
  }

  private static async Task<ScriptAlert> SingleAlert(TestApp testApp)
  {
    using var scope = testApp.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    return await db.ScriptAlerts.IgnoreQueryFilters().AsNoTracking().SingleAsync(Ct);
  }

  private sealed record Fixture(Guid TenantId, AppUser User, Guid DeviceId, Guid ScheduleId);
}
