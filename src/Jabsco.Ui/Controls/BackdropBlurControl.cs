using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace Jabsco.Ui.Controls;

// Portions adapted from rocksdanister/weather's BackdropBlurControl.cs.
// Source: https://github.com/rocksdanister/weather/blob/main/src/Drizzle.UI.Avalonia/UserControls/BackdropBlurControl.cs
//
// MIT License
//
// Copyright (c) 2023 Dani John
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
public class BackdropBlurControl : Control
{
    private const double DownsampleScale = 0.18;
    private const float BlurSigma = 2.2f;

    public static readonly StyledProperty<ExperimentalAcrylicMaterial?> MaterialProperty =
        AvaloniaProperty.Register<BackdropBlurControl, ExperimentalAcrylicMaterial?>(nameof(Material));

    public static readonly StyledProperty<byte[]?> FrameDataProperty =
        AvaloniaProperty.Register<BackdropBlurControl, byte[]?>(nameof(FrameData));

    public static readonly StyledProperty<Control?> SourceControlProperty =
        AvaloniaProperty.Register<BackdropBlurControl, Control?>(nameof(SourceControl));

    private static readonly ImmutableExperimentalAcrylicMaterial DefaultAcrylicMaterial =
        (ImmutableExperimentalAcrylicMaterial)new ExperimentalAcrylicMaterial
        {
            MaterialOpacity = 0.1,
            TintColor = Colors.White,
            TintOpacity = 0.1,
            PlatformTransparencyCompensationLevel = 0
        }.ToImmutable();

    private Bitmap? _backdropBitmap;
    private byte[]? _cachedFrameData;
    private Size _cachedBoundsSize;
    private Size _cachedSourceBounds;
    private Point? _cachedOriginInSource;

    private ImmutableExperimentalAcrylicMaterial? _cachedMaterial;
    private SolidColorBrush? _materialBrush;
    private SolidColorBrush? _tintBrush;

    public ExperimentalAcrylicMaterial? Material
    {
        get => GetValue(MaterialProperty);
        set => SetValue(MaterialProperty, value);
    }

    public byte[]? FrameData
    {
        get => GetValue(FrameDataProperty);
        set => SetValue(FrameDataProperty, value);
    }

    public Control? SourceControl
    {
        get => GetValue(SourceControlProperty);
        set => SetValue(SourceControlProperty, value);
    }

    static BackdropBlurControl()
    {
        AffectsRender<BackdropBlurControl>(MaterialProperty, FrameDataProperty, SourceControlProperty);
        FrameDataProperty.Changed.AddClassHandler<BackdropBlurControl>((control, _) => control.ClearBackdropCache());
        SourceControlProperty.Changed.AddClassHandler<BackdropBlurControl>((control, _) => control.ClearBackdropCache());
        MaterialProperty.Changed.AddClassHandler<BackdropBlurControl>((control, _) => control.ClearMaterialCache());
    }

    public override void Render(DrawingContext context)
    {
        var localBounds = new Rect(default, Bounds.Size);
        if (localBounds.Width <= 0 || localBounds.Height <= 0)
            return;

        var source = SourceControl;
        var originInSource = source == null ? null : this.TranslatePoint(default, source);
        var sourceBounds = source?.Bounds.Size ?? default;

        var backdrop = GetOrCreateBackdropBitmap(localBounds.Size, sourceBounds, originInSource);
        if (backdrop != null)
        {
            context.DrawImage(backdrop, localBounds);
        }

        DrawMaterialTint(context, localBounds);
    }

    private Bitmap? GetOrCreateBackdropBitmap(Size boundsSize, Size sourceBounds, Point? originInSource)
    {
        var frameData = FrameData;
        if (frameData == null || frameData.Length == 0)
        {
            ClearBackdropCache();
            return null;
        }

        var effectiveSourceBounds = sourceBounds.Width > 0 && sourceBounds.Height > 0
            ? sourceBounds
            : boundsSize;
        var effectiveOriginInSource = originInSource ?? default;

        if (ReferenceEquals(frameData, _cachedFrameData) &&
            boundsSize == _cachedBoundsSize &&
            effectiveSourceBounds == _cachedSourceBounds &&
            effectiveOriginInSource == _cachedOriginInSource)
        {
            return _backdropBitmap;
        }

        ClearBackdropCache();

        _backdropBitmap = CreateBackdropBitmap(frameData, boundsSize, effectiveSourceBounds, effectiveOriginInSource);
        _cachedFrameData = frameData;
        _cachedBoundsSize = boundsSize;
        _cachedSourceBounds = effectiveSourceBounds;
        _cachedOriginInSource = effectiveOriginInSource;

        return _backdropBitmap;
    }

    private static Bitmap? CreateBackdropBitmap(byte[] frameData, Size boundsSize, Size sourceBounds, Point origin)
    {
        using var frame = SKImage.FromEncodedData(frameData);
        if (frame == null || frame.Width <= 0 || frame.Height <= 0)
            return null;

        var outputWidth = Math.Max(1, (int)Math.Ceiling(boundsSize.Width * DownsampleScale));
        var outputHeight = Math.Max(1, (int)Math.Ceiling(boundsSize.Height * DownsampleScale));

        // Explicit BGRA8888 so the SKSurface pixel layout matches WriteableBitmap below.
        var info = new SKImageInfo(outputWidth, outputHeight, SKColorType.Bgra8888, SKAlphaType.Premul);

        using var sourceSurface = SKSurface.Create(info);
        if (sourceSurface == null)
            return null;

        sourceSurface.Canvas.Clear(SKColors.Black);

        var scale = Math.Min(sourceBounds.Width / frame.Width, sourceBounds.Height / frame.Height);
        if (scale <= 0)
            return null;

        var frameWidth = frame.Width * scale;
        var frameHeight = frame.Height * scale;
        var frameX = (sourceBounds.Width - frameWidth) / 2;
        var frameY = (sourceBounds.Height - frameHeight) / 2;

        var destRect = new SKRect(
            (float)((frameX - origin.X) * DownsampleScale),
            (float)((frameY - origin.Y) * DownsampleScale),
            (float)((frameX - origin.X + frameWidth) * DownsampleScale),
            (float)((frameY - origin.Y + frameHeight) * DownsampleScale));

        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
        sourceSurface.Canvas.DrawImage(frame, new SKRect(0, 0, frame.Width, frame.Height), destRect, sampling);

        using var sourceSnapshot = sourceSurface.Snapshot();

        // Render the blur pass directly into the WriteableBitmap's locked pixel buffer,
        // avoiding the encode → byte[] copy → decode round trip.
        var wb = new WriteableBitmap(
            new PixelSize(outputWidth, outputHeight),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using (var fb = wb.Lock())
        {
            using var blurredSurface = SKSurface.Create(info, fb.Address, fb.RowBytes);
            if (blurredSurface == null)
            {
                wb.Dispose();
                return null;
            }

            blurredSurface.Canvas.Clear(SKColors.Black);
            using var filter = SKImageFilter.CreateBlur(BlurSigma, BlurSigma, SKShaderTileMode.Clamp);
            using var blurPaint = new SKPaint { ImageFilter = filter, IsAntialias = false };
            blurredSurface.Canvas.DrawImage(sourceSnapshot, 0, 0, blurPaint);
            blurredSurface.Canvas.Flush();
        }

        return wb;
    }

    private void DrawMaterialTint(DrawingContext context, Rect bounds)
    {
        if (_cachedMaterial == null)
        {
            var material = Material != null
                ? (ImmutableExperimentalAcrylicMaterial)Material.ToImmutable()
                : DefaultAcrylicMaterial;
            _cachedMaterial = material;

            var mc = material.MaterialColor;
            var tc = material.TintColor;
            var materialAlpha = (byte)Math.Clamp(Math.Max((int)mc.A, 34), 0, 255);
            var tintAlpha = (byte)Math.Clamp(tc.A * 0.2, 0, 72);

            _materialBrush = new SolidColorBrush(Color.FromArgb(materialAlpha, mc.R, mc.G, mc.B));
            _tintBrush = new SolidColorBrush(Color.FromArgb(tintAlpha, tc.R, tc.G, tc.B));
        }

        context.FillRectangle(_materialBrush!, bounds);
        context.FillRectangle(_tintBrush!, bounds);
    }

    private void ClearMaterialCache()
    {
        _cachedMaterial = null;
        _materialBrush = null;
        _tintBrush = null;
    }

    private void ClearBackdropCache()
    {
        _backdropBitmap?.Dispose();
        _backdropBitmap = null;
        _cachedFrameData = null;
        _cachedBoundsSize = default;
        _cachedSourceBounds = default;
        _cachedOriginInSource = null;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ClearBackdropCache();
        base.OnDetachedFromVisualTree(e);
    }
}
