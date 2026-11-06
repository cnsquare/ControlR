using System.ComponentModel.DataAnnotations;
using ControlR.Web.Server.Data.Entities.Bases;

namespace ControlR.Web.Server.Data.Entities;

/// <summary>
/// One remote control session. The device and user are copied rather than linked, so the record
/// still reads correctly after either is deleted.
/// </summary>
public class RemoteSessionLog : TenantEntityBase
{
  public required Guid DeviceId { get; set; }

  [StringLength(256)]
  public required string DeviceName { get; set; }

  public DateTimeOffset? EndedAt { get; set; }

  [StringLength(512)]
  public string? FailureReason { get; set; }

  [StringLength(64)]
  public string? IpAddress { get; set; }

  public required Guid SessionId { get; set; }

  public required DateTimeOffset StartedAt { get; set; }

  public required Guid UserId { get; set; }

  [StringLength(256)]
  public required string UserName { get; set; }

  /// <summary>
  /// The server restarted while this session was open, so its end time is unknown.
  /// </summary>
  public bool WasInterrupted { get; set; }
}
