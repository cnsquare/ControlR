namespace ControlR.Web.Client.Components.Pages.DeviceAccess;

internal static class ScriptShellOptions
{
  private static readonly ScriptShell[] _unixShells = [ScriptShell.Bash, ScriptShell.PowerShell];
  private static readonly ScriptShell[] _windowsShells =
    [ScriptShell.WindowsPowerShell, ScriptShell.PowerShell, ScriptShell.Cmd];

  public static IReadOnlyList<ScriptShell> GetAvailableShells(SystemPlatform platform)
  {
    return platform == SystemPlatform.Windows ? _windowsShells : _unixShells;
  }

  public static string GetDisplayName(ScriptShell shell)
  {
    return shell switch
    {
      ScriptShell.WindowsPowerShell => "Windows PowerShell",
      ScriptShell.PowerShell => "PowerShell 7 (pwsh)",
      ScriptShell.Cmd => "Command Prompt",
      ScriptShell.Bash => "Bash",
      _ => shell.ToString(),
    };
  }

  public static Color GetStatusColor(ScriptResultStatus status)
  {
    return status switch
    {
      ScriptResultStatus.Succeeded => Color.Success,
      ScriptResultStatus.Failed => Color.Error,
      ScriptResultStatus.TimedOut => Color.Warning,
      _ => Color.Info,
    };
  }
}
