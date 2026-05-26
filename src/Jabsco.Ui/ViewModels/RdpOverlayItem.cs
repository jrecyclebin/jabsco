namespace Jabsco.Ui.ViewModels;

public enum OverlayKind { CursorTrail, ClickRing, TargetBox, AgentCursor }

public sealed class RdpOverlayItem
{
    public OverlayKind Kind { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    // TimeSpan.Zero means persistent — never expires
    public TimeSpan Lifetime { get; init; }

    public double AgeSeconds => (DateTimeOffset.UtcNow - CreatedAt).TotalSeconds;
    public double LifetimeFraction => Lifetime > TimeSpan.Zero ? Math.Clamp(AgeSeconds / Lifetime.TotalSeconds, 0, 1) : 0;
    public bool IsExpired => Lifetime > TimeSpan.Zero && AgeSeconds >= Lifetime.TotalSeconds;
    public double Opacity => Lifetime > TimeSpan.Zero ? 1.0 - LifetimeFraction : 1.0;
}
