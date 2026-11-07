using System.Runtime.Versioning;
using ControlR.Agent.Common.Services.Windows;

namespace ControlR.Agent.Common.Tests;

[SupportedOSPlatform("windows")]
public class ServiceRecoveryEnforcerTests
{
  [Fact]
  public void BuildFailureArguments_QuotesTheNameAndKeepsScSwitchSpacing()
  {
    var arguments = ServiceRecoveryEnforcerWin.BuildFailureArguments("ControlR.Agent (example.com)");

    Assert.Equal(
      "failure \"ControlR.Agent (example.com)\" reset= 86400 actions= restart/5000/restart/10000/restart/30000",
      arguments);
  }

  [Fact]
  public void BuildFailureArguments_RestartsMultipleTimes()
  {
    var arguments = ServiceRecoveryEnforcerWin.BuildFailureArguments("Svc");

    Assert.Equal(3, arguments.Split("restart/").Length - 1);
  }
}
