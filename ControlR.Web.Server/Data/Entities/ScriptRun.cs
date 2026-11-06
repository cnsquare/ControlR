using System.ComponentModel.DataAnnotations;
using ControlR.Web.Server.Data.Entities.Bases;

namespace ControlR.Web.Server.Data.Entities;

/// <summary>
/// One request to run a script on a set of devices. It doubles as the audit
/// record of what was run and who asked for it.
/// </summary>
public class ScriptRun : TenantEntityBase
{
  [StringLength(ScriptLimits.MaxContentLength)]
  public required string Content { get; set; }

  [StringLength(256)]
  public required string InitiatedBy { get; set; }

  public List<ScriptResult>? Results { get; set; }

  public required ScriptShell Shell { get; set; }

  public required int TimeoutSeconds { get; set; }
}
