namespace Jabsco.Core.Persistence.Profiles;

public sealed record Profile(
    int Id,
    string? Name,
    string Host,
    int Port,
    string? Username,
    string? CredentialRef,
    string Transport,
    string Resolution,
    string? LastModel,
    int? ToolPolicyId,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    int UseCount);
