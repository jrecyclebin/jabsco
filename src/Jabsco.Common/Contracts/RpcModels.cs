using System.Text.Json.Serialization;
using Jabsco.Common.Events;

namespace Jabsco.Common.Contracts;

public sealed record SessionCreateRequest(
    [property: JsonPropertyName("host")] string Host,
    [property: JsonPropertyName("credential_ref")] string? CredentialRef,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("idle_timeout_seconds")] int? IdleTimeoutSeconds,
    [property: JsonPropertyName("resolution")] string? Resolution);

public sealed record SessionCreateResponse(
    [property: JsonPropertyName("session_id")] string SessionId);

public sealed record SessionInfo(
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("host")] string Host,
    [property: JsonPropertyName("state")] ConnectionState State,
    [property: JsonPropertyName("last_activity")] DateTimeOffset LastActivity);

public sealed record SessionPromptRequest(
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("prompt")] string Prompt,
    [property: JsonPropertyName("tool_policy")] string? ToolPolicy);

public sealed record SessionCancelRequest(
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("mode")] string Mode);

public sealed record DaemonStatusResponse(
    [property: JsonPropertyName("uptime_seconds")] long UptimeSeconds,
    [property: JsonPropertyName("session_count")] int SessionCount,
    [property: JsonPropertyName("version")] string Version);
