using ControlR.Web.Server.Authz.Permissions;
using ControlR.Web.Server.Data;
using ControlR.Web.Server.Data.Entities;
using ControlR.Web.Server.Services.Authorization;
using ControlR.Web.Server.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace ControlR.Web.Server.Tests;

public class ClientPolicyGrantTests(ITestOutputHelper testOutput)
{
  private readonly ITestOutputHelper _testOutput = testOutput;

  [Fact]
  public async Task GetGrantedPolicies_DeviceScopedAllowDoesNotCreateGlobalGrant()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.App.Services.CreateTestTenant();
    await SeedFirstUser(testApp, tenant.Id);
    var user = await testApp.App.Services.CreateTestUser(tenant.Id);
    var device = await testApp.App.Services.CreateTestDevice(tenant.Id);

    await SeedAssignment(testApp, new PermissionAssignment
    {
      PrincipalKind = PermissionPrincipalKind.User,
      PrincipalId = user.Id,
      PermissionName = PermissionNames.DeviceTagsWrite,
      Effect = PermissionEffect.Allow,
      ScopeKind = PermissionScopeKind.Device,
      ScopeId = device.Id,
      OwningTenantId = tenant.Id,
      IsEnabled = true
    });

    var evaluator = GetEvaluator(testApp);
    var principal = CreateUserPrincipal(user.Id, tenant.Id);

    var result = await evaluator.GetGrantedPolicies(principal, TestContext.Current.CancellationToken);

    // DeviceTagsWrite maps to RequireTagsWrite only at tenant scope; a device-scoped allow must
    // not surface as a global client grant for tag management.
    Assert.DoesNotContain(PolicyNames.RequireTagsWrite, result);
  }

  [Fact]
  public async Task GetGrantedPolicies_NormalUserGetsNoUnrelatedPolicies()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.App.Services.CreateTestTenant();
    await SeedFirstUser(testApp, tenant.Id);
    var user = await testApp.App.Services.CreateTestUser(tenant.Id);

    var evaluator = GetEvaluator(testApp);
    var principal = CreateUserPrincipal(user.Id, tenant.Id);

    var result = await evaluator.GetGrantedPolicies(principal, TestContext.Current.CancellationToken);

    Assert.DoesNotContain(PolicyNames.RequireServerSettingsWrite, result);
  }

  [Fact]
  public async Task GetGrantedPolicies_ServerAdminGrantsServerPolicies()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.App.Services.CreateTestTenant();
    await SeedFirstUser(testApp, tenant.Id);
    var user = await testApp.App.Services.CreateTestUser(tenant.Id, presets: PermissionPresets.ServerAdministrator);

    var evaluator = GetEvaluator(testApp);
    var principal = CreateUserPrincipal(user.Id, tenant.Id);

    var result = await evaluator.GetGrantedPolicies(principal, TestContext.Current.CancellationToken);

    Assert.Contains(PolicyNames.RequireServerSettingsWrite, result);
    Assert.Contains(PolicyNames.RequireServerTelemetryRead, result);
  }

  [Fact]
  public async Task GetGrantedPolicies_TenantAdminGrantsTenantPolicies()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.App.Services.CreateTestTenant();
    await SeedFirstUser(testApp, tenant.Id);
    var user = await testApp.App.Services.CreateTestUser(tenant.Id, presets: PermissionPresets.TenantAdministrator);

    var evaluator = GetEvaluator(testApp);
    var principal = CreateUserPrincipal(user.Id, tenant.Id);

    var result = await evaluator.GetGrantedPolicies(principal, TestContext.Current.CancellationToken);

    Assert.Contains(PolicyNames.RequireCustomersRead, result);
    Assert.Contains(PolicyNames.RequireCustomersWrite, result);
  }

  [Fact]
  public async Task GetGrantedPolicies_TenantDenyRemovesMappedPolicy()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.App.Services.CreateTestTenant();
    await SeedFirstUser(testApp, tenant.Id);
    var user = await testApp.App.Services.CreateTestUser(tenant.Id);

    await SeedAssignment(testApp, new PermissionAssignment
    {
      PrincipalKind = PermissionPrincipalKind.User,
      PrincipalId = user.Id,
      PermissionName = PermissionNames.TenantCustomersRead,
      Effect = PermissionEffect.Allow,
      ScopeKind = PermissionScopeKind.Tenant,
      ScopeId = tenant.Id,
      OwningTenantId = tenant.Id,
      IsEnabled = true
    });

    await SeedAssignment(testApp, new PermissionAssignment
    {
      PrincipalKind = PermissionPrincipalKind.User,
      PrincipalId = user.Id,
      PermissionName = PermissionNames.TenantCustomersRead,
      Effect = PermissionEffect.Deny,
      ScopeKind = PermissionScopeKind.Tenant,
      ScopeId = tenant.Id,
      OwningTenantId = tenant.Id,
      IsEnabled = true
    });

    var evaluator = GetEvaluator(testApp);
    var principal = CreateUserPrincipal(user.Id, tenant.Id);

    var result = await evaluator.GetGrantedPolicies(principal, TestContext.Current.CancellationToken);

    Assert.DoesNotContain(PolicyNames.RequireCustomersRead, result);
  }

  [Fact]
  public async Task GetGrantedPolicies_LogonTokenGuest_GetsNoAdminPolicies()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.App.Services.CreateTestTenant();
    await SeedFirstUser(testApp, tenant.Id);
    // The user the invite impersonates is a full server + tenant admin.
    var admin = await testApp.App.Services.CreateTestUser(
      tenant.Id,
      presets: PermissionPresets.ServerAdministrator);
    var device = await testApp.App.Services.CreateTestDevice(tenant.Id);

    // The logon token's written grants: remote control + device read only, scoped to the device.
    var tokenId = Guid.NewGuid();
    await SeedLogonTokenScope(testApp, tokenId, PermissionNames.DeviceRemoteControlConnect, device.Id, tenant.Id);
    await SeedLogonTokenScope(testApp, tokenId, PermissionNames.DeviceRead, device.Id, tenant.Id);

    var evaluator = GetEvaluator(testApp);

    // The credential-scoped principal is exactly what the guest's cookie yields via ToPrincipalDescriptor,
    // and what IdentityRevalidatingAuthenticationStateProvider.OnPersistingAsync must use when it persists
    // client policies to the WebAssembly app. If credential scope is dropped here, the guest's UI becomes admin.
    var scopedGuest = new PrincipalDescriptor(
      PrincipalType.User,
      admin.Id,
      tenant.Id,
      AuthMethod: "logon-token",
      CredentialId: tokenId,
      CredentialType: CredentialType.LogonToken,
      DeviceScopeId: device.Id);

    var guestPolicies = await evaluator.GetGrantedPolicies(scopedGuest, TestContext.Current.CancellationToken);

    Assert.DoesNotContain(PolicyNames.RequireServerSettingsWrite, guestPolicies);
    Assert.DoesNotContain(PolicyNames.RequireCustomersWrite, guestPolicies);
    Assert.DoesNotContain(PolicyNames.RequireTenantSettingsWrite, guestPolicies);
    Assert.DoesNotContain(PolicyNames.RequireTenantUsersWrite, guestPolicies);

    // Sanity: the same admin user WITHOUT credential scoping is a full admin. This proves the scope is
    // what restricts the guest, and that the dropped-scope regression would expose the admin surface.
    var unscopedAdmin = new PrincipalDescriptor(PrincipalType.User, admin.Id, tenant.Id, AuthMethod: "cookie");
    var adminPolicies = await evaluator.GetGrantedPolicies(unscopedAdmin, TestContext.Current.CancellationToken);
    Assert.Contains(PolicyNames.RequireServerSettingsWrite, adminPolicies);
  }

  [Fact]
  public async Task Evaluate_LogonTokenGuest_ConfinedToScopedDeviceRemoteControl()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.App.Services.CreateTestTenant();
    await SeedFirstUser(testApp, tenant.Id);
    var admin = await testApp.App.Services.CreateTestUser(
      tenant.Id,
      presets: PermissionPresets.ServerAdministrator);
    var device = await testApp.App.Services.CreateTestDevice(tenant.Id);
    var otherDevice = await testApp.App.Services.CreateTestDevice(tenant.Id);

    var tokenId = Guid.NewGuid();
    await SeedLogonTokenScope(testApp, tokenId, PermissionNames.DeviceRemoteControlConnect, device.Id, tenant.Id);
    await SeedLogonTokenScope(testApp, tokenId, PermissionNames.DeviceRead, device.Id, tenant.Id);

    var evaluator = GetEvaluator(testApp);
    var resourceFactory = testApp.App.Services.GetRequiredService<IResourceDescriptorFactory>();
    var ct = TestContext.Current.CancellationToken;

    var scopedGuest = new PrincipalDescriptor(
      PrincipalType.User,
      admin.Id,
      tenant.Id,
      AuthMethod: "logon-token",
      CredentialId: tokenId,
      CredentialType: CredentialType.LogonToken,
      DeviceScopeId: device.Id);

    var deviceResource = await resourceFactory.CreateDevice(device, ct);
    var otherDeviceResource = await resourceFactory.CreateDevice(otherDevice, ct);
    var tenantResource = resourceFactory.CreateTenant(tenant.Id);

    // Allowed: remote control on the scoped device.
    Assert.True((await evaluator.Evaluate(scopedGuest, PermissionNames.DeviceRemoteControlConnect, deviceResource, ct)).Allowed);
    // Denied: a capability the token was not granted, even on the scoped device.
    Assert.False((await evaluator.Evaluate(scopedGuest, PermissionNames.DeviceScriptRun, deviceResource, ct)).Allowed);
    // Denied: remote control on any OTHER device.
    Assert.False((await evaluator.Evaluate(scopedGuest, PermissionNames.DeviceRemoteControlConnect, otherDeviceResource, ct)).Allowed);
    // Denied: tenant-level admin, despite impersonating a server admin.
    Assert.False((await evaluator.Evaluate(scopedGuest, PermissionNames.TenantCustomersRead, tenantResource, ct)).Allowed);
  }

  private static async Task SeedLogonTokenScope(
    TestApp testApp,
    Guid tokenId,
    string permissionName,
    Guid deviceId,
    Guid tenantId)
  {
    await SeedAssignment(testApp, new PermissionAssignment
    {
      PrincipalKind = PermissionPrincipalKind.LogonToken,
      PrincipalId = tokenId,
      PermissionName = permissionName,
      Effect = PermissionEffect.Allow,
      ScopeKind = PermissionScopeKind.Device,
      ScopeId = deviceId,
      OwningTenantId = tenantId,
      IsEnabled = true
    });
  }

  private static PrincipalDescriptor CreateUserPrincipal(Guid userId, Guid tenantId) =>
    new(PrincipalType.User, userId, tenantId, AuthMethod: "cookie");

  private static IPermissionEvaluator GetEvaluator(TestApp testApp) =>
    testApp.App.Services.GetRequiredService<IPermissionEvaluator>();

  private static async Task SeedAssignment(TestApp testApp, PermissionAssignment assignment)
  {
    using var scope = testApp.App.Services.CreateScope();
    await using var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    db.PermissionAssignments.Add(assignment);
    await db.SaveChangesAsync();
  }

  private static async Task SeedFirstUser(TestApp testApp, Guid tenantId)
  {
    await testApp.App.Services.CreateTestUser(
      tenantId,
      email: $"seed-{Guid.NewGuid():N}@t.local");
  }
}
