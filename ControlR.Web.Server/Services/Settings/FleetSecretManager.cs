using System.Security.Cryptography;
using System.Text;

namespace ControlR.Web.Server.Services.Settings;

/// <summary>
/// Manages each tenant's server-generated active fleet enrollment secret. This is the strong,
/// rotatable secret agents use for re-enrollment in steady state; unlike the baked-in bootstrap
/// secret it is never compiled into a binary. It rides the database (a tenant setting row), so it
/// does not survive a total database loss — recovery from that falls back to the bootstrap secret.
/// </summary>
public interface IFleetSecretManager
{
  /// <summary>Returns the tenant's active secret, generating and storing one if none exists.</summary>
  Task<string> GetOrCreate(Guid tenantId, CancellationToken cancellationToken = default);

  /// <summary>Replaces the tenant's active secret with a fresh one and returns it.</summary>
  Task<string> Rotate(Guid tenantId, CancellationToken cancellationToken = default);

  /// <summary>
  /// Constant-time compares a candidate against the tenant's stored active secret. False when the
  /// tenant has no stored secret (for example right after a database rebuild).
  /// </summary>
  Task<bool> Matches(Guid tenantId, string? candidate, CancellationToken cancellationToken = default);
}

public class FleetSecretManager(
  AppDb appDb,
  ILogger<FleetSecretManager> logger) : IFleetSecretManager
{
  private const string SettingName = TenantSettingNames.FleetEnrollmentSecret;
  private readonly AppDb _appDb = appDb;
  private readonly ILogger<FleetSecretManager> _logger = logger;

  public async Task<string> GetOrCreate(Guid tenantId, CancellationToken cancellationToken = default)
  {
    var existing = await Read(tenantId, cancellationToken);
    if (!string.IsNullOrWhiteSpace(existing))
    {
      return existing;
    }

    _logger.LogInformation("Generating a new active fleet enrollment secret for tenant {TenantId}.", tenantId);
    return await Store(tenantId, GenerateSecret(), cancellationToken);
  }

  public async Task<string> Rotate(Guid tenantId, CancellationToken cancellationToken = default)
  {
    _logger.LogInformation("Rotating the active fleet enrollment secret for tenant {TenantId}.", tenantId);
    return await Store(tenantId, GenerateSecret(), cancellationToken);
  }

  public async Task<bool> Matches(Guid tenantId, string? candidate, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(candidate))
    {
      return false;
    }

    var stored = await Read(tenantId, cancellationToken);
    if (string.IsNullOrWhiteSpace(stored))
    {
      return false;
    }

    return CryptographicOperations.FixedTimeEquals(
      Encoding.UTF8.GetBytes(stored),
      Encoding.UTF8.GetBytes(candidate));
  }

  private async Task<string?> Read(Guid tenantId, CancellationToken cancellationToken)
  {
    return await _appDb.TenantSettings
      .IgnoreQueryFilters()
      .AsNoTracking()
      .Where(x => x.TenantId == tenantId && x.Name == SettingName)
      .Select(x => x.Value)
      .FirstOrDefaultAsync(cancellationToken);
  }

  private async Task<string> Store(Guid tenantId, string secret, CancellationToken cancellationToken)
  {
    var entity = new TenantSetting
    {
      Name = SettingName,
      Value = secret,
      TenantId = tenantId,
    };

    var saved = await _appDb.AddOrUpdate(
      entity,
      x => x.Name == SettingName && x.TenantId == tenantId,
      cancellationToken);

    return saved.Value;
  }

  // Hex keeps the value inside the tenant-setting value regex (letters and digits only) while giving
  // 256 bits of entropy.
  private static string GenerateSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
