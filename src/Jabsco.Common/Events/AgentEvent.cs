using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jabsco.Common.Events;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ThinkingEvent), "thinking")]
[JsonDerivedType(typeof(ActionEvent), "action")]
[JsonDerivedType(typeof(ScreenshotEvent), "screenshot")]
[JsonDerivedType(typeof(ToolResultEvent), "tool_result")]
[JsonDerivedType(typeof(ApprovalRequestEvent), "approval_request")]
[JsonDerivedType(typeof(ConnectionEvent), "connection")]
[JsonDerivedType(typeof(FinalEvent), "final")]
[JsonDerivedType(typeof(ErrorEvent), "error")]
public abstract record AgentEvent(DateTimeOffset At, int Step);

public sealed record ThinkingEvent(string Text, DateTimeOffset At, int Step) : AgentEvent(At, Step);
public sealed record ActionEvent(AgentAction Action, DateTimeOffset At, int Step) : AgentEvent(At, Step);
// Live stream only; not written to transcript.
public sealed record ScreenshotEvent(byte[] PngBytes, DateTimeOffset At, int Step) : AgentEvent(At, Step);
public sealed record ToolResultEvent(string Summary, DateTimeOffset At, int Step) : AgentEvent(At, Step);
public sealed record ApprovalRequestEvent(string Tool, JsonElement Payload, DateTimeOffset At, int Step) : AgentEvent(At, Step);
public sealed record ConnectionEvent(ConnectionState State, DateTimeOffset At, int Step) : AgentEvent(At, Step);
public sealed record FinalEvent(string Response, AgentStats Stats, DateTimeOffset At, int Step) : AgentEvent(At, Step);
public sealed record ErrorEvent(string Message, string? ExceptionType, DateTimeOffset At, int Step) : AgentEvent(At, Step);
