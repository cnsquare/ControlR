using ControlR.Libraries.DataRedaction;

namespace ControlR.Agent.Shared.Options;

public class AgentAppOptions
{
  public const string SectionKey = "AppOptions";

  public string? AgentDisplayName { get; set; }

  /// <summary>
  /// The shared secret used to re-create this device on the server after a database loss. Empty
  /// means re-enrollment is off for this agent.
  /// </summary>
  [ProtectedDataClassification]
  public string? FleetEnrollmentSecret { get; set; }
  public Guid DeviceId { get; set; }
  
  [ProtectedDataClassification]
  public string? PrivateKey { get; set; }
  public Uri? ServerUri { get; set; }
  public Guid TenantId { get; set; }
}
