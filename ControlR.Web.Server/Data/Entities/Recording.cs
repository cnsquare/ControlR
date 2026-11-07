using System.ComponentModel.DataAnnotations;
using ControlR.Web.Server.Data.Entities.Bases;

namespace ControlR.Web.Server.Data.Entities;

/// <summary>
/// Metadata for a session recording. The video bytes live on the filesystem (see
/// <c>IRecordingStorage</c>); only this row rides the database.
/// </summary>
public class Recording : TenantEntityBase
{
  public Guid? DeviceId { get; set; }

  [StringLength(200)]
  public string? DeviceName { get; set; }

  public Guid? CreatedByUserId { get; set; }

  [StringLength(200)]
  public string? CreatedByUserName { get; set; }

  [StringLength(260)]
  public required string FileName { get; set; }

  [StringLength(100)]
  public required string ContentType { get; set; }

  public long SizeBytes { get; set; }

  public int? DurationSeconds { get; set; }
}
