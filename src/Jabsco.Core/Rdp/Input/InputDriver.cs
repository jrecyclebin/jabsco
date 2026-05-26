using Jabsco.Common.Events;
using Jabsco.Core.Rdp.Interop;

namespace Jabsco.Core.Rdp.Input;

internal sealed class InputDriver
{
    private readonly IntPtr _input;

    internal InputDriver(IntPtr input) => _input = input;

    internal void MouseMove(int x, int y) =>
        FreeRdpInputNative.SendMouseEvent(_input, FreeRdpInputNative.PtrMove, (ushort)x, (ushort)y);

    internal void MouseClick(MouseButton button, int x, int y, bool down)
    {
        ushort flags = button switch
        {
            MouseButton.Left   => FreeRdpInputNative.PtrButton1,
            MouseButton.Right  => FreeRdpInputNative.PtrButton2,
            MouseButton.Middle => FreeRdpInputNative.PtrButton3,
            _ => FreeRdpInputNative.PtrButton1
        };
        if (down) flags |= FreeRdpInputNative.PtrDown;
        FreeRdpInputNative.SendMouseEvent(_input, flags, (ushort)x, (ushort)y);
    }

    internal void MouseScroll(int x, int y, ScrollDirection direction, int amount)
    {
        ushort flags = FreeRdpInputNative.PtrWheel;
        int steps = amount * 120;
        if (direction == ScrollDirection.Down || direction == ScrollDirection.Right)
            flags |= FreeRdpInputNative.PtrWheelNeg;

        // Horizontal scroll uses extended mouse event
        if (direction is ScrollDirection.Left or ScrollDirection.Right)
            FreeRdpInputNative.SendExtendedMouseEvent(_input, (ushort)(flags | 0x0080), (ushort)x, (ushort)y);
        else
            FreeRdpInputNative.SendMouseEvent(_input, (ushort)(flags | (steps & 0xFF)), (ushort)x, (ushort)y);
    }

    internal void KeyChord(string keys)
    {
        var chords = KeyMap.ParseChord(keys);

        // Press all keys down in order
        foreach (var (scancode, extended) in chords)
        {
            ushort flags = FreeRdpInputNative.KbdDown;
            if (extended) flags |= FreeRdpInputNative.KbdExtended;
            FreeRdpInputNative.SendKeyboardEvent(_input, flags, (byte)scancode);
        }

        // Release in reverse
        foreach (var (scancode, extended) in chords.Reverse())
        {
            ushort flags = FreeRdpInputNative.KbdRelease;
            if (extended) flags |= FreeRdpInputNative.KbdExtended;
            FreeRdpInputNative.SendKeyboardEvent(_input, flags, (byte)scancode);
        }
    }

    internal void TypeChar(char c)
    {
        // \r and \n must go through the scancode path — RDP servers don't honour
        // Unicode keyboard events for control characters.
        if (c is '\r' or '\n')
        {
            const byte ReturnScancode = 0x1C;
            FreeRdpInputNative.SendKeyboardEvent(_input, FreeRdpInputNative.KbdDown,    ReturnScancode);
            FreeRdpInputNative.SendKeyboardEvent(_input, FreeRdpInputNative.KbdRelease, ReturnScancode);
            return;
        }

        FreeRdpInputNative.SendUnicodeKeyboardEvent(_input, 0, c);
        FreeRdpInputNative.SendUnicodeKeyboardEvent(_input, FreeRdpInputNative.KbdRelease, c);
    }
}
