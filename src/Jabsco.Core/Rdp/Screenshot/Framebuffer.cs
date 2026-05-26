using System.Runtime.InteropServices;

namespace Jabsco.Core.Rdp.Screenshot;

internal sealed class Framebuffer : IDisposable
{
    private byte[]? _buffer;
    private int _width;
    private int _height;
    private readonly Lock _lock = new();

    public void Update(IntPtr nativeBuffer, int width, int height)
    {
        int bytes = width * height * 4;
        lock (_lock)
        {
            if (_buffer == null || _buffer.Length != bytes)
                _buffer = new byte[bytes];
            Marshal.Copy(nativeBuffer, _buffer, 0, bytes);
            _width = width;
            _height = height;
        }
    }

    public byte[]? TryCopyPng()
    {
        byte[]? snap;
        int w, h;
        lock (_lock)
        {
            if (_buffer == null) return null;
            snap = new byte[_buffer.Length];
            _buffer.CopyTo(snap, 0);
            w = _width;
            h = _height;
        }
        return PngEncoder.EncodeBgra(snap, w, h);
    }

    public void Dispose() { }
}
