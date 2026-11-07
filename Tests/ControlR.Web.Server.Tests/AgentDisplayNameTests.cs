using ControlR.Libraries.Api.Contracts.Settings;
using ControlR.Web.Server.Services.Settings;
using ControlR.Web.Server.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace ControlR.Web.Server.Tests;

public class AgentDisplayNameTests(ITestOutputHelper testOutput)
{
  private readonly ITestOutputHelper _testOutput = testOutput;

  private static CancellationToken Ct => TestContext.Current.CancellationToken;

  [Theory]
  [InlineData("EnkrOma Support")]
  [InlineData("Acme-Remote 2.0")]
  [InlineData("agent_1")]
  [InlineData("")]
  [InlineData(null)]
  public void GetAgentDisplayNameError_AcceptableName_IsNull(string? name)
  {
    Assert.Null(TenantSettingDefinitions.GetAgentDisplayNameError(name));
  }

  [Theory]
  [InlineData("name\" & calc & \"")]
  [InlineData("a|b")]
  [InlineData("a;b")]
  [InlineData("a%PATH%")]
  [InlineData("$(whoami)")]
  [InlineData("a`b")]
  [InlineData("line\nbreak")]
  [InlineData("-flag")]
  [InlineData("Agènt")]
  public void GetAgentDisplayNameError_UnsafeName_IsRefused(string name)
  {
    Assert.NotNull(TenantSettingDefinitions.GetAgentDisplayNameError(name));
  }

  [Fact]
  public void GetAgentDisplayNameError_TooLong_IsRefused()
  {
    Assert.NotNull(TenantSettingDefinitions.GetAgentDisplayNameError(new string('a', 61)));
    Assert.Null(TenantSettingDefinitions.GetAgentDisplayNameError(new string('a', 60)));
  }

  [Fact]
  public async Task SetSetting_SavesAndClearsTheName()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    using var scope = testApp.Services.CreateScope();
    var manager = scope.ServiceProvider.GetRequiredService<ITenantSettingsManager>();

    var saved = await manager.SetSetting(
      tenant.Id,
      new InternalDtos.TenantSettingRequestDto(TenantSettingNames.AgentDisplayName, "  EnkrOma Support  "),
      Ct);

    Assert.True(saved.IsSuccess);
    Assert.Equal("EnkrOma Support", (await manager.GetAllSettings(tenant.Id, Ct)).AgentDisplayName);

    await manager.SetSetting(
      tenant.Id,
      new InternalDtos.TenantSettingRequestDto(TenantSettingNames.AgentDisplayName, string.Empty),
      Ct);

    Assert.Null((await manager.GetAllSettings(tenant.Id, Ct)).AgentDisplayName);
  }

  [Fact]
  public async Task SetSetting_UnsafeName_IsRefused()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    var tenant = await testApp.Services.CreateTestTenant();
    using var scope = testApp.Services.CreateScope();
    var manager = scope.ServiceProvider.GetRequiredService<ITenantSettingsManager>();

    var result = await manager.SetSetting(
      tenant.Id,
      new InternalDtos.TenantSettingRequestDto(TenantSettingNames.AgentDisplayName, "a & calc"),
      Ct);

    Assert.False(result.IsSuccess);
    Assert.Null((await manager.GetAllSettings(tenant.Id, Ct)).AgentDisplayName);
  }
}
