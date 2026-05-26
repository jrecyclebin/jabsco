using System.Text.Json;
using System.Text.Json.Serialization;
using Jabsco.Common.Contracts;
using Jabsco.Common.Events;

namespace Jabsco.Common.Json;

[JsonSerializable(typeof(AgentEvent))]
[JsonSerializable(typeof(ThinkingEvent))]
[JsonSerializable(typeof(ActionEvent))]
[JsonSerializable(typeof(ScreenshotEvent))]
[JsonSerializable(typeof(ToolResultEvent))]
[JsonSerializable(typeof(ApprovalRequestEvent))]
[JsonSerializable(typeof(ConnectionEvent))]
[JsonSerializable(typeof(FinalEvent))]
[JsonSerializable(typeof(ErrorEvent))]
[JsonSerializable(typeof(AgentAction))]
[JsonSerializable(typeof(ClickAction))]
[JsonSerializable(typeof(MouseMoveAction))]
[JsonSerializable(typeof(DragAction))]
[JsonSerializable(typeof(ScrollAction))]
[JsonSerializable(typeof(KeyAction))]
[JsonSerializable(typeof(TypeAction))]
[JsonSerializable(typeof(DoneAction))]
[JsonSerializable(typeof(ScreenshotAction))]
[JsonSerializable(typeof(AgentStats))]
[JsonSerializable(typeof(SessionCreateRequest))]
[JsonSerializable(typeof(SessionCreateResponse))]
[JsonSerializable(typeof(SessionInfo))]
[JsonSerializable(typeof(SessionPromptRequest))]
[JsonSerializable(typeof(SessionCancelRequest))]
[JsonSerializable(typeof(DaemonStatusResponse))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, WriteIndented = false)]
public partial class JabscoJsonContext : JsonSerializerContext { }
