using System.ComponentModel.DataAnnotations;
using ControlR.Web.Server.Data.Entities.Bases;

namespace ControlR.Web.Server.Data.Entities;

/// <summary>
/// One row per tenant. A tenant without a row, or with both thresholds at zero, gets no device alerts.
/// </summary>
public class AlertSettings : TenantEntityBase
{
  [StringLength(256)]
  public string? Email { get; set; }

  /// <summary>
  /// Alert when a disk has less than this percentage free. Zero turns the check off.
  /// </summary>
  public int LowDiskPercent { get; set; }

  /// <summary>
  /// Alert when a device has been offline for this long. Zero turns the check off.
  /// </summary>
  public int OfflineMinutes { get; set; }

  [StringLength(2048)]
  public string? WebhookUrl { get; set; }
}
