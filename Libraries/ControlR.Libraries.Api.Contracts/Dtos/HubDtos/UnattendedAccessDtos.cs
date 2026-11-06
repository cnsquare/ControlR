namespace ControlR.Libraries.Api.Contracts.Dtos.HubDtos;

[MessagePackObject(keyAsPropertyName: true)]
public record RequestUnattendedAccessRequestDto(Guid SessionId);

/// <summary>
/// A single-use, short-lived installer key for enrolling the device of the person who asked.
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
public record UnattendedAccessGrantDto(Guid TenantId, Guid KeyId, string KeySecret);
