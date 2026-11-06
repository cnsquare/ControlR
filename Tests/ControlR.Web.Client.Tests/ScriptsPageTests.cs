using System.Runtime.InteropServices;
using Bunit;
using ControlR.ApiClient;
using ControlR.ApiClient.Interfaces.V1;
using ControlR.Libraries.Api.Contracts.Dtos;
using ControlR.Libraries.Api.Contracts.Dtos.HubDtos;
using DeviceResponseDto = ControlR.Libraries.Api.Contracts.Dtos.ServerApi.Internal.DeviceResponseDto;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1;
using ControlR.Libraries.Api.Contracts.Enums;
using ControlR.Libraries.Viewer.Common.State;
using ControlR.Web.Client.Components.Layout.DeviceAccess;
using ControlR.Web.Client.Components.Pages.DeviceAccess;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MudBlazor;
using MudBlazor.Services;

namespace ControlR.Web.Client.Tests;

public class ScriptsPageTests
{
  private static readonly Guid _deviceId = Guid.NewGuid();
  private static readonly Guid _runId = Guid.NewGuid();
  private static readonly Guid _tenantId = Guid.NewGuid();

  [Fact]
  public void CanAccess_FollowsRunScriptsPermissionOnly()
  {
    var allowed = Permissions(canUseTerminal: false, canRunScripts: true);
    var denied = Permissions(canUseTerminal: true, canRunScripts: false);

    Assert.True(DeviceAccessPagePermissions.CanAccess(allowed, ClientRoutes.DeviceAccessScripts));
    Assert.False(DeviceAccessPagePermissions.CanAccess(denied, ClientRoutes.DeviceAccessScripts));
  }

  [Fact]
  public async Task CreateFailure_ShowsNoResult()
  {
    await using var ctx = new BunitContext();
    var api = new Mock<IScriptRunsApi>();
    api
      .Setup(x => x.CreateScriptRun(_tenantId, It.IsAny<CreateScriptRunRequestDto>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(ApiResult.Fail<ScriptRunDto>("Forbidden."));
    var cut = RenderPage(ctx, api, SystemPlatform.Linux);

    cut.Find("textarea").Input("echo hi");
    await ClickRun(cut);

    cut.WaitForAssertion(() => Assert.Contains("Run", RunButton(cut).TextContent));
    Assert.Empty(cut.FindAll("pre"));
    api.Verify(
      x => x.GetScriptRun(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
      Times.Never);
  }

  [Theory]
  [InlineData(SystemPlatform.Windows, ScriptShell.WindowsPowerShell)]
  [InlineData(SystemPlatform.Linux, ScriptShell.Bash)]
  [InlineData(SystemPlatform.MacOs, ScriptShell.Bash)]
  public async Task Run_DefaultsToShellForDevicePlatform(SystemPlatform platform, ScriptShell expected)
  {
    await using var ctx = new BunitContext();
    var api = new Mock<IScriptRunsApi>();
    api
      .Setup(x => x.CreateScriptRun(_tenantId, It.IsAny<CreateScriptRunRequestDto>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(ApiResult.Ok(Run(Result(ScriptResultStatus.Succeeded))));
    var cut = RenderPage(ctx, api, platform);

    cut.Find("textarea").Input("echo hi");
    await ClickRun(cut);

    api.Verify(x => x.CreateScriptRun(
      _tenantId,
      It.Is<CreateScriptRunRequestDto>(r =>
        r.Shell == expected &&
        r.Content == "echo hi" &&
        r.DeviceIds.Single() == _deviceId),
      It.IsAny<CancellationToken>()));
  }

  [Fact]
  public async Task History_ShowsRunsAndViewLoadsOutputAndScript()
  {
    await using var ctx = new BunitContext();
    var api = new Mock<IScriptRunsApi>();
    api
      .Setup(x => x.GetScriptRun(_runId, _tenantId, It.IsAny<CancellationToken>()))
      .ReturnsAsync(ApiResult.Ok(Run(Result(ScriptResultStatus.Succeeded, exitCode: 0, stdout: "past output"))));
    var cut = RenderPage(ctx, api, SystemPlatform.Linux, HistoryItem(ScriptResultStatus.Succeeded));

    Assert.Contains("tester", cut.Markup);
    Assert.Contains("echo preview", cut.Find(".script-preview").TextContent);

    await cut.FindAll("button").Single(x => x.TextContent.Contains("View")).ClickAsync(new());

    cut.WaitForAssertion(() => Assert.Contains("past output", cut.Find("pre").TextContent));
    Assert.Equal("echo hi", cut.Find("textarea").TextContent);
  }

  [Fact]
  public async Task History_ViewingPendingRun_ResumesPolling()
  {
    await using var ctx = new BunitContext();
    var api = new Mock<IScriptRunsApi>();
    api
      .SetupSequence(x => x.GetScriptRun(_runId, _tenantId, It.IsAny<CancellationToken>()))
      .ReturnsAsync(ApiResult.Ok(Run(Result(ScriptResultStatus.Pending))))
      .ReturnsAsync(ApiResult.Ok(Run(Result(ScriptResultStatus.Succeeded, exitCode: 0, stdout: "finished"))));
    var cut = RenderPage(ctx, api, SystemPlatform.Linux, HistoryItem(ScriptResultStatus.Pending));

    await cut.FindAll("button").Single(x => x.TextContent.Contains("View")).ClickAsync(new());

    cut.WaitForAssertion(() => Assert.Contains("finished", cut.Find("pre").TextContent), TimeSpan.FromSeconds(10));
  }

  [Fact]
  public async Task History_WhenEmpty_SaysSo()
  {
    await using var ctx = new BunitContext();
    var cut = RenderPage(ctx, new Mock<IScriptRunsApi>(), SystemPlatform.Linux);

    Assert.Contains("No scripts have been run", cut.Markup);
  }

  [Fact]
  public async Task Run_PendingResult_PollsUntilOutputArrives()
  {
    await using var ctx = new BunitContext();
    var api = new Mock<IScriptRunsApi>();
    api
      .Setup(x => x.CreateScriptRun(_tenantId, It.IsAny<CreateScriptRunRequestDto>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(ApiResult.Ok(Run(Result(ScriptResultStatus.Pending))));
    api
      .Setup(x => x.GetScriptRun(_runId, _tenantId, It.IsAny<CancellationToken>()))
      .ReturnsAsync(ApiResult.Ok(Run(Result(ScriptResultStatus.Succeeded, exitCode: 0, stdout: "hello"))));
    var cut = RenderPage(ctx, api, SystemPlatform.Linux);

    cut.Find("textarea").Input("echo hello");
    await ClickRun(cut);

    cut.WaitForAssertion(() => Assert.Contains("hello", cut.Find("pre").TextContent), TimeSpan.FromSeconds(10));
    Assert.Contains("Exit code 0", cut.Markup);
    Assert.Contains("Succeeded", cut.Markup);
  }

  [Fact]
  public async Task RunButton_IsDisabledUntilScriptIsEntered()
  {
    await using var ctx = new BunitContext();
    var cut = RenderPage(ctx, new Mock<IScriptRunsApi>(), SystemPlatform.Linux);

    Assert.True(RunButton(cut).HasAttribute("disabled"));

    cut.Find("textarea").Input("echo hi");

    Assert.False(RunButton(cut).HasAttribute("disabled"));
  }

  [Fact]
  public async Task ScriptOutput_IsRenderedAsTextNotMarkup()
  {
    await using var ctx = new BunitContext();
    var api = new Mock<IScriptRunsApi>();
    var hostile = "<img src=x onerror=alert(1)>";
    api
      .Setup(x => x.CreateScriptRun(_tenantId, It.IsAny<CreateScriptRunRequestDto>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(ApiResult.Ok(Run(Result(ScriptResultStatus.Failed, exitCode: 1, stdout: hostile, stderr: hostile))));
    var cut = RenderPage(ctx, api, SystemPlatform.Linux);

    cut.Find("textarea").Input("echo hi");
    await ClickRun(cut);

    cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("pre").Count));
    Assert.Empty(cut.FindAll("img"));
    Assert.Equal(hostile, cut.FindAll("pre")[0].TextContent);
  }

  [Fact]
  public void WindowsDevices_DoNotOfferBash_AndUnixDevicesDoNotOfferCmd()
  {
    Assert.DoesNotContain(ScriptShell.Bash, ScriptShellOptions.GetAvailableShells(SystemPlatform.Windows));
    Assert.DoesNotContain(ScriptShell.Cmd, ScriptShellOptions.GetAvailableShells(SystemPlatform.Linux));
    Assert.DoesNotContain(ScriptShell.WindowsPowerShell, ScriptShellOptions.GetAvailableShells(SystemPlatform.MacOs));
  }

  private static async Task ClickRun(IRenderedComponent<Scripts> cut)
  {
    await RunButton(cut).ClickAsync(new());
  }

  private static DeviceResponseDto Device(SystemPlatform platform)
  {
    return new DeviceResponseDto(
      Name: "device",
      AgentVersion: "1.0.0",
      CpuUtilization: 0,
      Id: _deviceId,
      Is64Bit: true,
      IsOnline: true,
      LastSeen: DateTimeOffset.UtcNow,
      OsArchitecture: Architecture.X64,
      Platform: platform,
      ProcessorCount: 1,
      ConnectionId: "connection",
      OsDescription: "os",
      TenantId: _tenantId,
      TotalMemory: 0,
      TotalStorage: 0,
      UsedMemory: 0,
      UsedStorage: 0,
      CurrentUsers: [],
      MacAddresses: [],
      PublicIpV4: string.Empty,
      PublicIpV6: string.Empty,
      LocalIpV4: string.Empty,
      LocalIpV6: string.Empty,
      Drives: [],
      IsOutdated: false);
  }

  private static ScriptRunHistoryItemDto HistoryItem(ScriptResultStatus status)
  {
    return new ScriptRunHistoryItemDto(
      _runId,
      ScriptShell.Bash,
      "echo preview",
      "tester",
      DateTimeOffset.UtcNow,
      status,
      ExitCode: null);
  }

  private static DeviceAccessPermissionsDto Permissions(bool canUseTerminal, bool canRunScripts)
  {
    return new DeviceAccessPermissionsDto(
      CanViewOverview: false,
      CanUseRemoteControl: false,
      CanUseTerminal: canUseTerminal,
      CanUseChat: false,
      CanReadFileSystem: false,
      CanReadLogs: false,
      CanUseVncRelay: false,
      CanInteractWithRemoteControl: false,
      CanBlockRemoteInput: false,
      CanReadClipboard: false,
      CanWriteClipboard: false,
      CanSendCtrlAltDel: false,
      CanTogglePrivacyScreen: false,
      CanRunScripts: canRunScripts);
  }

  private static IRenderedComponent<Scripts> RenderPage(
    BunitContext ctx,
    Mock<IScriptRunsApi> scriptRuns,
    SystemPlatform platform,
    params ScriptRunHistoryItemDto[] history)
  {
    scriptRuns
      .Setup(x => x.GetScriptRunHistory(_deviceId, _tenantId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(ApiResult.Ok(history));

    var v1 = new Mock<IControlrV1Api>();
    v1.SetupGet(x => x.ScriptRuns).Returns(scriptRuns.Object);
    var api = new Mock<IControlrApi>();
    api.SetupGet(x => x.V1).Returns(v1.Object);

    var device = Device(platform);
    var deviceState = new Mock<IDeviceState>();
    deviceState.SetupGet(x => x.CurrentDevice).Returns(device);
    deviceState.Setup(x => x.TryGetCurrentDevice()).Returns(device);

    ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    ctx.Services.AddMudServices();
    ctx.Services.AddSingleton(api.Object);
    ctx.Services.AddSingleton(deviceState.Object);
    ctx.Services.AddSingleton<ILogger<Scripts>>(NullLogger<Scripts>.Instance);

    ctx.Render<MudPopoverProvider>();

    var uri = ctx.Services.GetRequiredService<NavigationManager>()
      .GetUriWithQueryParameter("deviceId", _deviceId.ToString());
    ctx.Services.GetRequiredService<NavigationManager>().NavigateTo(uri);

    return ctx.Render<Scripts>();
  }

  private static ScriptResultDto Result(
    ScriptResultStatus status,
    int? exitCode = null,
    string stdout = "",
    string stderr = "")
  {
    return new ScriptResultDto(
      Guid.NewGuid(),
      _deviceId,
      status,
      exitCode,
      stdout,
      stderr,
      OutputTruncated: false,
      FailureReason: null,
      StartedAt: null,
      FinishedAt: null);
  }

  private static ScriptRunDto Run(ScriptResultDto result)
  {
    return new ScriptRunDto(_runId, ScriptShell.Bash, "echo hi", 300, "tester", DateTimeOffset.UtcNow, [result]);
  }

  private static AngleSharp.Dom.IElement RunButton(IRenderedComponent<Scripts> cut)
  {
    return cut.FindAll("button").Single(x => x.TextContent.Contains("Run", StringComparison.Ordinal));
  }
}
