namespace Jabsco.Common.Events;

public abstract record AgentAction;
public sealed record ScreenshotAction : AgentAction;
public sealed record ClickAction(MouseButton Button, int X, int Y, int Clicks = 1) : AgentAction;
public sealed record MouseMoveAction(int X, int Y) : AgentAction;
public sealed record DragAction(int StartX, int StartY, int EndX, int EndY) : AgentAction;
public sealed record ScrollAction(int X, int Y, ScrollDirection Direction, int Amount = 3) : AgentAction;
public sealed record KeyAction(string Keys) : AgentAction;
public sealed record TypeAction(string Text) : AgentAction;
public sealed record DoneAction(string Response) : AgentAction;
public sealed record LoadSkillAction(string SkillName) : AgentAction;
public sealed record WaitAction(int Seconds) : AgentAction;

// Change the active connection. Exactly one of: connect a saved profile by name, connect a
// VM on the current host by id, or disconnect.
public sealed record SwitchAction(string? Profile = null, Guid? VmId = null, bool Disconnect = false) : AgentAction;

// A lifecycle action on a Hyper-V VM. VmId null means the currently-connected VM.
public sealed record VmAction(VmOperation Operation, Guid? VmId = null) : AgentAction;

// Create or alter a Hyper-V VM. VmId present → alter (only provided fields change);
// absent → create a new VM from Name + Generation.
public sealed record VmSetupAction(VmSpec Spec, Guid? VmId = null) : AgentAction;

public enum VmGeneration { Gen1 = 1, Gen2 = 2 }

// Every field is nullable so an alter changes only what's provided. Sizes carry their unit
// in the name. Checkpoints/GuestServices/EnhancedSession are live-safe; the rest are
// hardware-class and need the VM stopped (see RequiresStoppedVm).
public sealed record VmSpec(
    string? Name = null,
    VmGeneration? Generation = null,
    int? MemoryMB = null,
    int? VhdSizeGB = null,
    string? IsoPath = null,
    bool? Tpm = null,
    bool? SecureBoot = null,
    int? ProcessorCount = null,
    string? NetworkAdapter = null,
    bool? Checkpoints = null,
    bool? GuestServices = null,
    bool? EnhancedSession = null)
{
    public bool RequiresStoppedVm =>
        Generation is not null || MemoryMB is not null || VhdSizeGB is not null ||
        IsoPath is not null || Tpm is not null || SecureBoot is not null ||
        ProcessorCount is not null || NetworkAdapter is not null;
}

// Shutdown is graceful (via integration services); PowerOff is a forced power-off.
public enum VmOperation { Start, Shutdown, PowerOff, Save, Pause, Resume, Restart }

public static class VmOperations
{
    public static VmOperation? Parse(string? value) => value?.ToLowerInvariant() switch
    {
        "start"   => VmOperation.Start,
        "shutdown" => VmOperation.Shutdown,
        "poweroff" => VmOperation.PowerOff,
        "save"    => VmOperation.Save,
        "pause"   => VmOperation.Pause,
        "resume"  => VmOperation.Resume,
        "restart" => VmOperation.Restart,
        _         => null
    };

    public static string ToToken(this VmOperation op) => op switch
    {
        VmOperation.Start   => "start",
        VmOperation.Shutdown => "shutdown",
        VmOperation.PowerOff => "poweroff",
        VmOperation.Save    => "save",
        VmOperation.Pause   => "pause",
        VmOperation.Resume  => "resume",
        VmOperation.Restart => "restart",
        _                   => "start"
    };
}

public enum MouseButton { Left, Right, Middle }
public enum ScrollDirection { Up, Down, Left, Right }
