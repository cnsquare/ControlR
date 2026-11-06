using System.ComponentModel.DataAnnotations;
using ControlR.Web.Server.Data.Entities.Bases;

namespace ControlR.Web.Server.Data.Entities;

public class Tenant : EntityBase
{
  public List<Customer>? Customers { get; set; }
  public List<DeviceGroup>? DeviceGroups { get; set; }
  public List<Device>? Devices { get; set; }
  public List<LogonToken>? LogonTokens { get; set; }

  [StringLength(100)]
  public string? Name { get; set; }
  public List<SavedScript>? SavedScripts { get; set; }
  public List<RemoteSessionLog>? RemoteSessionLogs { get; set; }
  public List<ScriptAlert>? ScriptAlerts { get; set; }
  public List<ScriptRun>? ScriptRuns { get; set; }
  public List<ScriptSchedule>? ScriptSchedules { get; set; }
  public List<Tag>? Tags { get; set; }
  public List<TenantInvite>? TenantInvites { get; set; }
  public List<TenantSetting>? TenantSettings { get; set; }
  public List<UserGroup>? UserGroups { get; set; }
  public List<AppUser>? Users { get; set; }
}
