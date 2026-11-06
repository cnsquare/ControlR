using System.ComponentModel.DataAnnotations;
using ControlR.Web.Server.Data.Entities.Bases;

namespace ControlR.Web.Server.Data.Entities;

/// <summary>
/// A problem with a schedule, on one device or with the schedule as a whole. Repeat failures
/// update the open alert, so a schedule failing every hour is one alert with a count.
/// </summary>
public class ScriptAlert : TenantEntityBase
{
  public Guid? DeviceId { get; set; }

  [StringLength(256)]
  public string? DeviceName { get; set; }

  public DateTimeOffset? DismissedAt { get; set; }

  [StringLength(256)]
  public string? DismissedBy { get; set; }

  public required DateTimeOffset LastOccurredAt { get; set; }

  [StringLength(512)]
  public required string Message { get; set; }

  public int Occurrences { get; set; } = 1;

  public required Guid ScheduleId { get; set; }

  [StringLength(ScriptLimits.MaxNameLength)]
  public required string ScheduleName { get; set; }

  public Guid? ScriptRunId { get; set; }
}
