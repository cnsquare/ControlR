using System.ComponentModel.DataAnnotations;
using ControlR.Web.Server.Data.Entities.Bases;

namespace ControlR.Web.Server.Data.Entities;

public class SavedScript : TenantEntityBase
{
  [StringLength(ScriptLimits.MaxContentLength)]
  public required string Content { get; set; }

  [StringLength(256)]
  public required string CreatedBy { get; set; }

  [StringLength(ScriptLimits.MaxNameLength)]
  public required string Name { get; set; }

  public required ScriptShell Shell { get; set; }

  public required int TimeoutSeconds { get; set; }
}
