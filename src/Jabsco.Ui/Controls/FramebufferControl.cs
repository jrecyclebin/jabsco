using System;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Jabsco.Ui.Controls;

public sealed class FramebufferControl : Control
{
    public static readonly StyledProperty<byte[]?> FrameDataProperty =
        AvaloniaProperty.Register<FramebufferControl, byte[]?>(nameof(FrameData));

    private Bitmap? _bitmap;

    public byte[]? FrameData
    {
        get => GetValue(FrameDataProperty);
        set => SetValue(FrameDataProperty, value);
    }

    static FramebufferControl()
    {
        FrameDataProperty.Changed.AddClassHandler<FramebufferControl>((c, _) => c.OnFrameDataChanged());
        AffectsRender<FramebufferControl>(FrameDataProperty);
    }

    private void OnFrameDataChanged()
    {
        _bitmap?.Dispose();
        _bitmap = null;

        if (FrameData is { Length: > 0 } data)
        {
            try
            {
                using var ms = new MemoryStream(data);
                _bitmap = new Bitmap(ms);
            }
            catch
            {
                // Invalid frame data — leave bitmap null
            }
        }
        InvalidateVisual();
    }

    // Translates a control-space point to RDP framebuffer coordinates, accounting for letterboxing.
    public (int x, int y) ToRdpCoords(Point controlPoint)
    {
        if (_bitmap == null) return (0, 0);
        var bw = _bitmap.PixelSize.Width;
        var bh = _bitmap.PixelSize.Height;
        double scale = Math.Min(Bounds.Width / bw, Bounds.Height / bh);
        double destX = (Bounds.Width - bw * scale) / 2;
        double destY = (Bounds.Height - bh * scale) / 2;
        int rdpX = (int)Math.Clamp((controlPoint.X - destX) / scale, 0, bw - 1);
        int rdpY = (int)Math.Clamp((controlPoint.Y - destY) / scale, 0, bh - 1);
        return (rdpX, rdpY);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);

        if (_bitmap == null)
        {
            context.FillRectangle(Brushes.Black, bounds);
            var ft = new FormattedText(
                "No signal",
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Monospace"),
                14,
                Brushes.Gray);
            context.DrawText(ft, new Point(
                bounds.Width / 2 - ft.Width / 2,
                bounds.Height / 2 - ft.Height / 2));
            return;
        }

        var bw = _bitmap.PixelSize.Width;
        var bh = _bitmap.PixelSize.Height;
        double scale = Math.Min(bounds.Width / bw, bounds.Height / bh);
        var destW = bw * scale;
        var destH = bh * scale;
        var destX = (bounds.Width - destW) / 2;
        var destY = (bounds.Height - destH) / 2;

        context.FillRectangle(Brushes.Black, bounds);
        context.DrawImage(_bitmap, new Rect(destX, destY, destW, destH));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
