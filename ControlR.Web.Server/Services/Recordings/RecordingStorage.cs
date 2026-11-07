
namespace ControlR.Web.Server.Services.Recordings;

/// <summary>
/// Stores and retrieves session recording video files on the filesystem, partitioned by tenant.
/// </summary>
public interface IRecordingStorage
{
  Task<long> Save(Guid tenantId, string fileName, Stream content, CancellationToken cancellationToken);
  bool TryGetPath(Guid tenantId, string fileName, out string fullPath);
  void Delete(Guid tenantId, string fileName);
}

public class RecordingStorage(
  IOptionsMonitor<AppOptions> appOptions,
  ILogger<RecordingStorage> logger) : IRecordingStorage
{
  private readonly IOptionsMonitor<AppOptions> _appOptions = appOptions;
  private readonly ILogger<RecordingStorage> _logger = logger;

  public async Task<long> Save(Guid tenantId, string fileName, Stream content, CancellationToken cancellationToken)
  {
    var dir = GetTenantDirectory(tenantId);
    Directory.CreateDirectory(dir);
    var fullPath = Path.Combine(dir, SanitizeFileName(fileName));

    await using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
    await content.CopyToAsync(fileStream, cancellationToken);
    await fileStream.FlushAsync(cancellationToken);
    return fileStream.Length;
  }

  public bool TryGetPath(Guid tenantId, string fileName, out string fullPath)
  {
    fullPath = Path.Combine(GetTenantDirectory(tenantId), SanitizeFileName(fileName));
    return File.Exists(fullPath);
  }

  public void Delete(Guid tenantId, string fileName)
  {
    try
    {
      var fullPath = Path.Combine(GetTenantDirectory(tenantId), SanitizeFileName(fileName));
      if (File.Exists(fullPath))
      {
        File.Delete(fullPath);
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to delete recording file {FileName} for tenant {TenantId}.", fileName, tenantId);
    }
  }

  private string GetTenantDirectory(Guid tenantId) =>
    Path.Combine(_appOptions.CurrentValue.RecordingsStoragePath, tenantId.ToString("N"));

  // The stored name is server-generated ("{guid}.{ext}"), but guard against traversal regardless.
  private static string SanitizeFileName(string fileName) => Path.GetFileName(fileName);
}
