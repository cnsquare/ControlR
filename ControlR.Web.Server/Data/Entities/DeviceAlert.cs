using System.ComponentModel.DataAnnotations;
using ControlR.Web.Server.Data.Entities.Bases;

namespace ControlR.Web.Server.Data.Entities;

/// <summary>
/// A device problem. It stays until the condition clears, even once dismissed, so dismissing
/// a retired device stops it being raised again.
/// </summary>
public class DeviceAlert : TenantEntityBase
{
  public required Guid DeviceId { get; set; }

  [StringLength(256)]
  public required string DeviceName { get; set; }

  public DateTimeOffset? DismissedAt { get; set; }

  [StringLength(256)]
  public string? DismissedBy { get; set; }

  public required DeviceAlertKind Kind { get; set; }

  [StringLength(512)]
  public required string Message { get; set; }

  public required DateTimeOffset RaisedAt { get; set; }

  public DateTimeOffset? ResolvedAt { get; set; }
}
