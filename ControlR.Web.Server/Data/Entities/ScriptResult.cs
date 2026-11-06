using System.ComponentModel.DataAnnotations;
using ControlR.Web.Server.Data.Entities.Bases;

namespace ControlR.Web.Server.Data.Entities;

/// <summary>
/// The outcome of a <see cref="ScriptRun"/> on one device.
/// </summary>
public class ScriptResult : TenantEntityBase
{
  public Device? Device { get; set; }

  public required Guid DeviceId { get; set; }

  public int? ExitCode { get; set; }

  [StringLength(512)]
  public string? FailureReason { get; set; }

  /// <summary>
  /// Set only by the agent's report. A dispatch failure leaves this null so a
  /// late report from an agent that did run the script can still be recorded.
  /// </summary>
  public DateTimeOffset? FinishedAt { get; set; }

  public bool OutputTruncated { get; set; }

  public ScriptRun? ScriptRun { get; set; }

  public required Guid ScriptRunId { get; set; }

  public string StandardError { get; set; } = string.Empty;

  public string StandardOutput { get; set; } = string.Empty;

  public DateTimeOffset? StartedAt { get; set; }

  public ScriptResultStatus Status { get; set; } = ScriptResultStatus.Pending;
}
