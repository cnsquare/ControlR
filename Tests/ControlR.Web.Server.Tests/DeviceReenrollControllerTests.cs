using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using ControlR.Libraries.Api.Contracts.Dtos.Devices;
using ControlR.Libraries.Api.Contracts.Dtos.HubDtos;
using ControlR.Libraries.Shared.Services.Encryption;
using ControlR.Web.Server.Data;
using ControlR.Web.Server.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ControlR.Web.Server.Tests;

public class DeviceReenrollControllerTests(ITestOutputHelper testOutput)
{
  private const string ValidSecret = "a-very-long-fleet-secret-value";
  private readonly ITestOutputHelper _testOutput = testOutput;

  [Fact]
  public async Task Reenroll_NoSecretConfigured_IsInertAndForbidden()
  {
    using var testServer = await TestWebServerBuilder.CreateTestServer(_testOutput);
    using var httpClient = await testServer.GetHttpClient();
    var services = testServer.Services;

    var tenant = await services.CreateTestTenant();
    var (deviceDto, publicKey) = CreateDeviceAndKey(services, tenant.Id);

    var request = new InternalDtos.ReenrollDeviceRequestDto(deviceDto, publicKey, ValidSecret);
    var response = await httpClient.PostAsJsonAsync(
      HttpConstants.Agent.DeviceReenrollEndpoint, request, TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

    using var scope = services.CreateScope();
    await using var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    var dbDevice = await db.Devices
      .IgnoreQueryFilters()
      .FirstOrDefaultAsync(d => d.Id == deviceDto.Id, cancellationToken: TestContext.Current.CancellationToken);
    Assert.Null(dbDevice);
  }

  [Fact]
  public async Task Reenroll_WithCorrectSecret_RecreatesDevice()
  {
    using var testServer = await CreateServerWithSecret();
    using var httpClient = await testServer.GetHttpClient();
    var services = testServer.Services;

    var tenant = await services.CreateTestTenant();
    var (deviceDto, publicKey) = CreateDeviceAndKey(services, tenant.Id);

    var request = new InternalDtos.ReenrollDeviceRequestDto(deviceDto, publicKey, ValidSecret);
    var response = await httpClient.PostAsJsonAsync(
      HttpConstants.Agent.DeviceReenrollEndpoint, request, TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var result = await response.Content.ReadFromJsonAsync<InternalDtos.DeviceResponseDto>(
      cancellationToken: TestContext.Current.CancellationToken);
    Assert.NotNull(result);
    Assert.Equal(deviceDto.Id, result.Id);

    using var scope = services.CreateScope();
    await using var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    var dbDevice = await db.Devices
      .IgnoreQueryFilters()
      .FirstOrDefaultAsync(d => d.Id == deviceDto.Id, cancellationToken: TestContext.Current.CancellationToken);
    Assert.NotNull(dbDevice);
    Assert.Equal(tenant.Id, dbDevice.TenantId);
    Assert.Equal(publicKey, dbDevice.PublicKey);
  }

  [Fact]
  public async Task Reenroll_WithWrongSecret_Forbidden()
  {
    using var testServer = await CreateServerWithSecret();
    using var httpClient = await testServer.GetHttpClient();
    var services = testServer.Services;

    var tenant = await services.CreateTestTenant();
    var (deviceDto, publicKey) = CreateDeviceAndKey(services, tenant.Id);

    var request = new InternalDtos.ReenrollDeviceRequestDto(deviceDto, publicKey, "the-wrong-secret-value-here");
    var response = await httpClient.PostAsJsonAsync(
      HttpConstants.Agent.DeviceReenrollEndpoint, request, TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

    using var scope = services.CreateScope();
    await using var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    var dbDevice = await db.Devices
      .IgnoreQueryFilters()
      .FirstOrDefaultAsync(d => d.Id == deviceDto.Id, cancellationToken: TestContext.Current.CancellationToken);
    Assert.Null(dbDevice);
  }

  [Fact]
  public async Task Reenroll_WithInvalidPublicKey_BadRequest()
  {
    using var testServer = await CreateServerWithSecret();
    using var httpClient = await testServer.GetHttpClient();
    var services = testServer.Services;

    var tenant = await services.CreateTestTenant();
    var (deviceDto, _) = CreateDeviceAndKey(services, tenant.Id);

    var request = new InternalDtos.ReenrollDeviceRequestDto(deviceDto, "not-a-valid-base64-key", ValidSecret);
    var response = await httpClient.PostAsJsonAsync(
      HttpConstants.Agent.DeviceReenrollEndpoint, request, TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  private Task<TestWebServer> CreateServerWithSecret()
  {
    var settings = new Dictionary<string, string?>
    {
      ["AppOptions:FleetEnrollmentSecret"] = ValidSecret,
    };
    return TestWebServerBuilder.CreateTestServer(_testOutput, settings: settings);
  }

  private static (DeviceUpdateRequestDto Device, string PublicKey) CreateDeviceAndKey(
    IServiceProvider services, Guid tenantId)
  {
    var keyProvider = services.GetRequiredService<IEd25519KeyProvider>();
    var keyPair = keyProvider.GenerateKeyPair();
    var publicKey = Convert.ToBase64String(keyPair.PublicKey);
    return (CreateDeviceDto(tenantId), publicKey);
  }

  private static DeviceUpdateRequestDto CreateDeviceDto(Guid tenantId, Guid? deviceId = null)
  {
    var id = deviceId ?? Guid.NewGuid();
    return new DeviceUpdateRequestDto(
      Name: $"Test Device {id}",
      AgentVersion: "1.0.0",
      CpuUtilization: 25.0,
      Id: id,
      Is64Bit: true,
      OsArchitecture: Architecture.X64,
      Platform: SystemPlatform.Windows,
      ProcessorCount: 8,
      OsDescription: "Windows 11 Pro",
      TenantId: tenantId,
      TotalMemory: 16384,
      TotalStorage: 512000,
      UsedMemory: 8192,
      UsedStorage: 256000,
      CurrentUsers: ["TestUser"],
      MacAddresses: ["00:11:22:33:44:55"],
      LocalIpV4: "10.0.0.1",
      LocalIpV6: "fe80::1",
      Drives: [new Drive { Name = "C:", VolumeLabel = "System", TotalSize = 512000, FreeSpace = 256000 }]
    );
  }
}
