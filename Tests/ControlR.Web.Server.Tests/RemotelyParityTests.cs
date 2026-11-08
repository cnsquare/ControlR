using ControlR.Web.Server.Api.V1;
using ControlR.Web.Server.Authz.Permissions;
using ControlR.Web.Server.Data;
using ControlR.Web.Server.Data.Entities;
using ControlR.Web.Server.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ControlR.Web.Server.Tests;

public class RemotelyParityTests(ITestOutputHelper testOutput)
{
  private readonly ITestOutputHelper _testOutput = testOutput;

  private static CancellationToken Ct => TestContext.Current.CancellationToken;

  [Fact]
  public async Task DeviceNotes_DeviceInAnotherTenant_IsNotFound()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    await testApp.Services.SeedServerAdminAsync();
    var tenant = await testApp.Services.CreateTestTenant();
    var otherTenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);
    var foreignDevice = await testApp.Services.CreateTestDevice(otherTenant.Id);

    using var scope = testApp.Services.CreateScope();
    var controller = await scope.CreateControllerWithUser<DeviceNotesController>(user);

    var response = await controller.UpdateDeviceNotes(foreignDevice.Id, new("hacked"), tenant.Id, Ct);

    ActionResultAsserts.AssertHttpStatus(response.Result, 404);
  }

  [Fact]
  public async Task DeviceNotes_ReadPermissionAlone_CanReadButNotWrite()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    await testApp.Services.SeedServerAdminAsync();
    var tenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    await Grant(testApp, user, device.Id, PermissionNames.DeviceRead);

    using var scope = testApp.Services.CreateScope();
    var controller = await scope.CreateControllerWithUser<DeviceNotesController>(user);

    var read = await controller.GetDeviceNotes(device.Id, tenant.Id, Ct);
    var write = await controller.UpdateDeviceNotes(device.Id, new("mine"), tenant.Id, Ct);

    Assert.IsType<OkObjectResult>(read.Result);
    Assert.IsType<ForbidResult>(write.Result);
  }

  [Fact]
  public async Task DeviceNotes_SurviveAnAgentHeartbeatUpdate()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    await testApp.Services.SeedServerAdminAsync();
    var tenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    await Grant(testApp, user, device.Id, PermissionNames.DeviceRead);
    await Grant(testApp, user, device.Id, PermissionNames.DeviceNotesWrite);

    using (var scope = testApp.Services.CreateScope())
    {
      var controller = await scope.CreateControllerWithUser<DeviceNotesController>(user);
      Assert.IsType<OkObjectResult>((await controller.UpdateDeviceNotes(device.Id, new("Front desk PC"), tenant.Id, Ct)).Result);
    }

    // Registering the same device id again is the path an agent update takes.
    await testApp.Services.CreateTestDevice(tenant.Id, device.Id);

    using var readScope = testApp.Services.CreateScope();
    var db = readScope.ServiceProvider.GetRequiredService<AppDb>();
    var stored = await db.Devices.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == device.Id, Ct);
    Assert.Equal("Front desk PC", stored.Notes);
  }

  [Fact]
  public async Task Postgres_NotesAndFolders_PersistAndSort()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(
      _testOutput,
      testDatabaseName: $"{Guid.NewGuid()}",
      useInMemoryDatabase: false);
    var tenant = await testApp.Services.CreateTestTenant();
    var user = await testApp.Services.CreateTestUser(tenant.Id);
    var device = await testApp.Services.CreateTestDevice(tenant.Id);
    await Grant(testApp, user, device.Id, PermissionNames.DeviceNotesWrite);

    using var scope = testApp.Services.CreateScope();
    var notes = await scope.CreateControllerWithUser<DeviceNotesController>(user);
    Assert.IsType<OkObjectResult>((await notes.UpdateDeviceNotes(device.Id, new("Note"), tenant.Id, Ct)).Result);

    var scripts = await scope.CreateControllerWithUser<SavedScriptsController>(user);
    await scripts.CreateSavedScript(new("Zebra", ScriptShell.Bash, "echo z", Folder: "Alpha"), tenant.Id, Ct);
    await scripts.CreateSavedScript(new("Apple", ScriptShell.Bash, "echo a", Folder: "  Beta  "), tenant.Id, Ct);

    var response = await scripts.GetSavedScripts(tenant.Id, Ct);
    var list = Assert.IsType<V1Dtos.SavedScriptDto[]>(Assert.IsType<OkObjectResult>(response.Result).Value);

    Assert.Equal(["Zebra", "Apple"], list.Select(x => x.Name));
    Assert.Equal("Beta", list[1].Folder);
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
}
