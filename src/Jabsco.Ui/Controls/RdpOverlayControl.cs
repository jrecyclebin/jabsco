using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Jabsco.Ui.ViewModels;

namespace Jabsco.Ui.Controls;

public sealed class RdpOverlayControl : Control
{
    public static readonly StyledProperty<double> RdpWidthProperty =
        AvaloniaProperty.Register<RdpOverlayControl, double>(nameof(RdpWidth), 1280);

    public static readonly StyledProperty<double> RdpHeightProperty =
        AvaloniaProperty.Register<RdpOverlayControl, double>(nameof(RdpHeight), 800);

    public static readonly StyledProperty<ObservableCollection<RdpOverlayItem>?> ItemsProperty =
        AvaloniaProperty.Register<RdpOverlayControl, ObservableCollection<RdpOverlayItem>?>(nameof(Items));

    public double RdpWidth
    {
        get => GetValue(RdpWidthProperty);
        set => SetValue(RdpWidthProperty, value);
    }

    public double RdpHeight
    {
        get => GetValue(RdpHeightProperty);
        set => SetValue(RdpHeightProperty, value);
    }

    public ObservableCollection<RdpOverlayItem>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    private DispatcherTimer? _timer;

    static RdpOverlayControl()
    {
        ItemsProperty.Changed.AddClassHandler<RdpOverlayControl>((c, _) => c.InvalidateVisual());
        AffectsRender<RdpOverlayControl>(ItemsProperty);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(33),
            DispatcherPriority.Render,
            (_, _) =>
            {
                if (Items != null)
                {
                    var expired = Items.Where(i => i.IsExpired).ToList();
                    foreach (var item in expired) Items.Remove(item);
                }
                InvalidateVisual();
            });
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer?.Stop();
        _timer = null;
        base.OnDetachedFromVisualTree(e);
    }

    private (double ox, double oy, double scale) GetTransform()
    {
        var bw = RdpWidth;
        var bh = RdpHeight;
        double scale = Math.Min(Bounds.Width / bw, Bounds.Height / bh);
        double ox = (Bounds.Width - bw * scale) / 2;
        double oy = (Bounds.Height - bh * scale) / 2;
        return (ox, oy, scale);
    }

    private Point ToControl(double rdpX, double rdpY)
    {
        var (ox, oy, scale) = GetTransform();
        return new Point(ox + rdpX * scale, oy + rdpY * scale);
    }

    public override void Render(DrawingContext ctx)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        if (Items == null) return;

        var (_, _, scale) = GetTransform();

        foreach (var item in Items.ToList())
        {
            var pt = ToControl(item.X, item.Y);
            double alpha = item.Opacity;
            if (alpha <= 0) continue;

            switch (item.Kind)
            {
                case OverlayKind.CursorTrail:
                {
                    double r = 4 * scale;
                    ctx.DrawEllipse(
                        new SolidColorBrush(new Color((byte)(alpha * 200), 80, 80, 80)),
                        null, pt, r, r);
                    break;
                }
                case OverlayKind.ClickRing:
                {
                    double progress = item.LifetimeFraction;
                    double radius = (8 + 24 * progress) * scale;
                    double strokeOpacity = alpha * 0.9;
                    var pen = new Pen(
                        new SolidColorBrush(new Color((byte)(strokeOpacity * 255), 0, 120, 215)),
                        2.0 * scale);
                    ctx.DrawEllipse(null, pen, pt, radius, radius);
                    break;
                }
                case OverlayKind.TargetBox:
                {
                    double half = 12 * scale;
                    var rect = new Rect(pt.X - half, pt.Y - half, half * 2, half * 2);
                    var pen = new Pen(
                        new SolidColorBrush(new Color((byte)(alpha * 255), 0, 120, 215)),
                        1.5 * scale,
                        dashStyle: DashStyle.Dash);
                    ctx.DrawRectangle(null, pen, rect, 2, 2);
                    break;
                }
                case OverlayKind.AgentCursor:
                {
                    DrawAgentCursor(ctx, pt);
                    break;
                }
            }
        }
    }

    // Classic arrow cursor (tip at pt), fixed 22px height — red fill, white outline.
    private static void DrawAgentCursor(DrawingContext ctx, Point tip)
    {
        double x = tip.X, y = tip.Y;
        var geo = new StreamGeometry();
        using var sgc = geo.Open();
        sgc.BeginFigure(new Point(x,      y),      true);
        sgc.LineTo(     new Point(x,      y + 17));
        sgc.LineTo(     new Point(x + 4,  y + 13));
        sgc.LineTo(     new Point(x + 7,  y + 20));
        sgc.LineTo(     new Point(x + 9,  y + 19));
        sgc.LineTo(     new Point(x + 6,  y + 12));
        sgc.LineTo(     new Point(x + 11, y + 12));
        sgc.EndFigure(true);

        ctx.DrawGeometry(Brushes.Crimson, new Pen(Brushes.White, 1.5), geo);
    }
}
