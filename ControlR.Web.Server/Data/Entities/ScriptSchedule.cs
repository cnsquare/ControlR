using System.ComponentModel.DataAnnotations;
using ControlR.Web.Server.Data.Entities.Bases;

namespace ControlR.Web.Server.Data.Entities;

/// <summary>
/// Runs a script on devices at a fixed interval, on the authority of the user who created it.
/// The script is copied here rather than referenced. Otherwise anyone able to edit the library
/// could run code on devices they cannot reach, through someone else's schedule.
/// </summary>
public class ScriptSchedule : TenantEntityBase
{
  [StringLength(256)]
  public string? AlertEmail { get; set; }

  [StringLength(ScriptLimits.MaxContentLength)]
  public required string Content { get; set; }

  [StringLength(256)]
  public required string CreatedBy { get; set; }

  public required Guid CreatedByUserId { get; set; }

  public required List<Guid> DeviceIds { get; set; }

  public required ScriptScheduleInterval Interval { get; set; }

  public bool IsEnabled { get; set; } = true;

  [StringLength(512)]
  public string? LastError { get; set; }

  public DateTimeOffset? LastRun { get; set; }

  public Guid? LastRunId { get; set; }

  [StringLength(ScriptLimits.MaxNameLength)]
  public required string Name { get; set; }

  public required DateTimeOffset NextRun { get; set; }

  [StringLength(ScriptLimits.MaxNameLength)]
  public required string ScriptName { get; set; }

  public bool RunOnNextConnect { get; set; }

  public required ScriptShell Shell { get; set; }

  public required int TimeoutSeconds { get; set; }
}
