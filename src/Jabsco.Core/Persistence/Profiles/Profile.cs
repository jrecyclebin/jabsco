namespace Jabsco.Core.Persistence.Profiles;

public sealed record Profile(
    int Id,
    string? Name,
    string Host,
    int Port,
    Guid? VmId,
    string? Username,
    string? CredentialRef,
    string Transport,
    string Resolution,
    string? LastModel,
    int? ToolPolicyId,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    int UseCount);
