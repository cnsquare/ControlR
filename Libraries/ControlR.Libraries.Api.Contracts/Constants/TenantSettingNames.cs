namespace ControlR.Libraries.Api.Contracts.Constants;

public static class TenantSettingNames
{
  public const string AgentDisplayName = "agent-display-name";
  public const string AppendInstanceId = "append-instance-id";
  /// <summary>
  /// The tenant's server-generated active fleet enrollment secret. Internal and server-managed, not
  /// a user-facing setting: it is stored as a tenant setting row only so it rides the database, and
  /// is deliberately absent from <c>TenantSettingDefinitions</c> so it never surfaces in the UI.
  /// </summary>
  public const string FleetEnrollmentSecret = "fleet-enrollment-secret";
  public const string InstanceId = "instance-id";
  public const string NotifyUserOnSessionStart = "notify-user-on-session-start";
}