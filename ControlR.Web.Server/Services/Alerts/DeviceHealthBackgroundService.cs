using ControlR.Libraries.Branding;
using ControlR.Libraries.Hosting;

namespace ControlR.Web.Server.Services.Alerts;

/// <summary>
/// Compares devices with each tenant's alert settings. The first check waits a full period after
/// startup, which gives agents time to reconnect to a server that has just restarted.
/// </summary>
public class DeviceHealthBackgroundService(
  IDbContextFactory<AppDb> dbContextFactory,
  IServiceScopeFactory scopeFactory,
  TimeProvider timeProvider,
  ILogger<PeriodicBackgroundService> logger)
  : PeriodicBackgroundService(TimeSpan.FromMinutes(5), true, timeProvider, logger)
{
  private const int MaxLinesPerNotification = 20;

  private static readonly TimeSpan _resolvedRetention = TimeSpan.FromDays(90);

  private readonly IDbContextFactory<AppDb> _dbContextFactory = dbContextFactory;
  private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
  private readonly TimeProvider _timeProvider = timeProvider;

  public async Task<int> CheckDevices(CancellationToken cancellationToken = default)
  {
    await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
    var allSettings = await db.AlertSettings.AsNoTracking().ToListAsync(cancellationToken);

    var raised = 0;
    foreach (var settings in allSettings)
    {
      raised += await CheckTenant(db, settings, cancellationToken);
    }

    var cutoff = _timeProvider.GetUtcNow() - _resolvedRetention;
    await db.DeviceAlerts
      .Where(x => x.ResolvedAt < cutoff)
      .ExecuteDeleteCompatAsync(db, cancellationToken);

    return raised;
  }

  protected override async Task HandleElapsed(CancellationToken stoppingToken)
  {
    await CheckDevices(stoppingToken);
  }

  private static string? FindLowDisk(Device device, int lowDiskPercent)
  {
    var low = device.Drives
      .Where(x => x.DriveType == System.IO.DriveType.Fixed && x.TotalSize > 0)
      .Select(x => (Drive: x, FreePercent: x.FreeSpace / x.TotalSize * 100))
      .Where(x => x.FreePercent < lowDiskPercent)
      .OrderBy(x => x.FreePercent)
      .FirstOrDefault();

    return low.Drive is null
      ? null
      : $"{low.Drive.Name} has {low.FreePercent:0.#}% free.";
  }

  private async Task<int> CheckTenant(AppDb db, AlertSettings settings, CancellationToken cancellationToken)
  {
    var now = _timeProvider.GetUtcNow();

    var devices = await db.Devices
      .AsNoTracking()
      .Where(x => x.TenantId == settings.TenantId)
      .ToDictionaryAsync(x => x.Id, cancellationToken);

    var open = await db.DeviceAlerts
      .Where(x => x.TenantId == settings.TenantId && x.ResolvedAt == null)
      .ToListAsync(cancellationToken);

    foreach (var alert in open)
    {
      if (!devices.TryGetValue(alert.DeviceId, out var device) || IsResolved(alert, device, settings))
      {
        alert.ResolvedAt = now;
      }
    }

    var added = new List<DeviceAlert>();
    foreach (var device in devices.Values)
    {
      if (settings.OfflineMinutes > 0 &&
          !device.IsOnline &&
          device.LastSeen < now - TimeSpan.FromMinutes(settings.OfflineMinutes))
      {
        Raise(DeviceAlertKind.Offline, $"Offline since {device.LastSeen:u}.");
      }

      // The drive figures of an offline device are old, so they prove nothing either way.
      if (settings.LowDiskPercent > 0 &&
          device.IsOnline &&
          FindLowDisk(device, settings.LowDiskPercent) is { } message)
      {
        Raise(DeviceAlertKind.LowDisk, message);
      }

      void Raise(DeviceAlertKind kind, string text)
      {
        var existing = open.Find(x => x.DeviceId == device.Id && x.Kind == kind && x.ResolvedAt == null);
        if (existing is not null)
        {
          existing.Message = text;
          return;
        }

        var alert = new DeviceAlert
        {
          Id = Guid.NewGuid(),
          DeviceId = device.Id,
          DeviceName = device.Name,
          Kind = kind,
          Message = text,
          RaisedAt = now,
          TenantId = settings.TenantId,
        };

        db.DeviceAlerts.Add(alert);
        added.Add(alert);
      }
    }

    await db.SaveChangesAsync(cancellationToken);

    if (added.Count > 0)
    {
      await Notify(settings, added, cancellationToken);
    }

    return added.Count;
  }

  private static bool IsResolved(DeviceAlert alert, Device device, AlertSettings settings)
  {
    return alert.Kind switch
    {
      DeviceAlertKind.Offline => settings.OfflineMinutes <= 0 || device.IsOnline,
      DeviceAlertKind.LowDisk => settings.LowDiskPercent <= 0 ||
        (device.IsOnline && FindLowDisk(device, settings.LowDiskPercent) is null),
      _ => true,
    };
  }

  private async Task Notify(AlertSettings settings, List<DeviceAlert> alerts, CancellationToken cancellationToken)
  {
    var lines = alerts
      .Take(MaxLinesPerNotification)
      .Select(x => $"{x.DeviceName}: {x.Message}")
      .ToList();

    if (alerts.Count > MaxLinesPerNotification)
    {
      lines.Add($"And {alerts.Count - MaxLinesPerNotification} more. See the Alerts page.");
    }

    using var scope = _scopeFactory.CreateScope();
    var notifier = scope.ServiceProvider.GetRequiredService<IAlertNotifier>();
    await notifier.Notify(settings, $"{BrandingConstants.BrandName} device alerts", lines, cancellationToken);
  }
}
