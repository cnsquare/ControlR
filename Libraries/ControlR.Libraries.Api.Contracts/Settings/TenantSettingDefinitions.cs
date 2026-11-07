using System.Collections.Frozen;
using System.Globalization;
using ControlR.Libraries.Api.Contracts.Constants;

namespace ControlR.Libraries.Api.Contracts.Settings;

public static class TenantSettingDefinitions
{
  private const int MaxAgentDisplayNameLength = 60;

  /// <summary>
  /// Every setting, in <see cref="InternalDtos.TenantSettingsDto"/> constructor order.
  /// </summary>
  public static IReadOnlyList<ISettingDefinition> All =>
  [
    AppendInstanceId,
    InstanceId,
    NotifyUserOnSessionStart,
    AgentDisplayName
  ];
  public static SettingDefinition<string?> AgentDisplayName { get; } =
    new(
      TenantSettingNames.AgentDisplayName,
      null,
      value => ParseResult<string?>.Success(string.IsNullOrWhiteSpace(value) ? null : value.Trim()),
      validate: GetAgentDisplayNameError,
      invalidValueMessageFactory: settingName => $"{settingName} has an invalid value.");
  public static SettingDefinition<bool?> AppendInstanceId { get; } =
    new(
      TenantSettingNames.AppendInstanceId,
      null,
      value => bool.TryParse(value, out var parsedValue)
        ? ParseResult<bool?>.Success(parsedValue)
        : ParseResult<bool?>.Failure(null),
      invalidValueMessageFactory: settingName => $"{settingName} must be a valid boolean value.");
  public static SettingDefinition<string?> InstanceId { get; } =
    new(
      TenantSettingNames.InstanceId,
      null,
      value => ParseResult<string?>.Success(string.IsNullOrWhiteSpace(value) ? null : value.Trim()),
      validate: ValidateInstanceId,
      invalidValueMessageFactory: settingName => $"{settingName} has an invalid value.");
  public static SettingDefinition<bool?> NotifyUserOnSessionStart { get; } =
    new(
      TenantSettingNames.NotifyUserOnSessionStart,
      null,
      value => bool.TryParse(value, out var parsedValue)
        ? ParseResult<bool?>.Success(parsedValue)
        : ParseResult<bool?>.Failure(null),
      invalidValueMessageFactory: settingName => $"{settingName} must be a valid boolean value.");

  private static FrozenDictionary<string, ISettingDefinition> DefinitionsByName { get; } =
    All.ToFrozenDictionary(x => x.Name, StringComparer.Ordinal);

  public static TenantSettingsDto CreateDto(
    IReadOnlyDictionary<string, string> values,
    Action<string, string>? onInvalidValue = null)
  {
    TValue Read<TValue>(SettingDefinition<TValue> definition)
    {
      return definition.ReadValue(values, value => onInvalidValue?.Invoke(definition.Name, value));
    }

    return new TenantSettingsDto(
      Read(AppendInstanceId),
      Read(InstanceId),
      Read(NotifyUserOnSessionStart),
      Read(AgentDisplayName));
  }

  /// <summary>
  /// The agent applies this name to the operating system with SYSTEM rights, so it is limited to
  /// characters that mean nothing to a shell. The agent checks it again rather than trust the server.
  /// </summary>
  public static string? GetAgentDisplayNameError(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return null;
    }

    var name = value.Trim();
    if (name.Length > MaxAgentDisplayNameLength)
    {
      return $"The agent name can be at most {MaxAgentDisplayNameLength} characters.";
    }

    if (!char.IsAsciiLetterOrDigit(name[0]))
    {
      return "The agent name must start with a letter or number.";
    }

    return name.All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '.' or '_' or '-')
      ? null
      : "The agent name can only use letters, numbers, spaces, periods, hyphens and underscores.";
  }

  public static string? FormatValue(string name, object? value)
  {
    if (DefinitionsByName.TryGetValue(name, out var definition))
    {
      return definition.FormatObjectValue(value);
    }

    return value switch
    {
      null => null,
      IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
      _ => Convert.ToString(value, CultureInfo.InvariantCulture)
    };
  }

  public static IReadOnlyList<(string Name, string? Value)> GetValues(TenantSettingsDto settings)
  {
    return
    [
      (AppendInstanceId.Name, AppendInstanceId.FormatValue(settings.AppendInstanceId)),
      (InstanceId.Name, InstanceId.FormatValue(settings.InstanceId)),
      (NotifyUserOnSessionStart.Name, NotifyUserOnSessionStart.FormatValue(settings.NotifyUserOnSessionStart)),
      (AgentDisplayName.Name, AgentDisplayName.FormatValue(settings.AgentDisplayName))
    ];
  }

  public static SettingValueNormalizationResult Normalize(string name, string value)
  {
    if (DefinitionsByName.TryGetValue(name, out var definition))
    {
      return definition.Normalize(value);
    }

    return SettingValueNormalizationResult.Success(value.Trim());
  }

  private static string? ValidateInstanceId(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return null;
    }

    var trimmedValue = value.Trim();
    if (string.Equals(trimmedValue, "default", StringComparison.OrdinalIgnoreCase))
    {
      return "Instance ID 'default' is reserved.";
    }

    if (trimmedValue is "." or "..")
    {
      return "Instance ID cannot be '.' or '..'.";
    }

    if (trimmedValue.Contains(Path.DirectorySeparatorChar) || trimmedValue.Contains(Path.AltDirectorySeparatorChar))
    {
      return "Instance ID must not contain path separators.";
    }

    var invalidCharacters = trimmedValue
      .Where(c => !char.IsLetterOrDigit(c) && c is not '.' and not '_' and not '-')
      .Distinct()
      .ToArray();

    if (invalidCharacters.Length == 0)
    {
      return null;
    }

    return $"Instance ID contains one or more invalid characters: {string.Join(", ", invalidCharacters)}";
  }
}
