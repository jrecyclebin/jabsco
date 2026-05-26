using System.Runtime.InteropServices;
using SkiaSharp;

namespace Jabsco.Core.Rdp.Screenshot;

internal static class PngEncoder
{
    internal static byte[] EncodeBgra(byte[] bgraData, int width, int height)
    {
        using var bmp = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        Marshal.Copy(bgraData, 0, bmp.GetPixels(), bgraData.Length);
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }
}
