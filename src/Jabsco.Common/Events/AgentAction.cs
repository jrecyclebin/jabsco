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

public enum MouseButton { Left, Right, Middle }
public enum ScrollDirection { Up, Down, Left, Right }
