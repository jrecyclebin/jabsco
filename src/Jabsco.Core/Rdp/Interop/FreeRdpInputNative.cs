using System.Runtime.InteropServices;

namespace Jabsco.Core.Rdp.Interop;

internal static partial class FreeRdpInputNative
{
    private const string Lib = "freerdp3";

    // code is UINT8 (byte) per freerdp/input.h — scancode fits in one byte
    [LibraryImport(Lib, EntryPoint = "freerdp_input_send_keyboard_event")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SendKeyboardEvent(IntPtr input, ushort flags, byte code);

    [LibraryImport(Lib, EntryPoint = "freerdp_input_send_mouse_event")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SendMouseEvent(IntPtr input, ushort flags, ushort x, ushort y);

    [LibraryImport(Lib, EntryPoint = "freerdp_input_send_extended_mouse_event")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SendExtendedMouseEvent(IntPtr input, ushort flags, ushort x, ushort y);

    [LibraryImport(Lib, EntryPoint = "freerdp_input_send_unicode_keyboard_event")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SendUnicodeKeyboardEvent(IntPtr input, ushort flags, ushort code);

    // Keyboard flags
    internal const ushort KbdDown     = 0x4000;
    internal const ushort KbdRelease  = 0x8000;
    internal const ushort KbdExtended = 0x0100;

    // Mouse flags
    internal const ushort PtrMove    = 0x0800;
    internal const ushort PtrButton1 = 0x1000;   // left
    internal const ushort PtrButton2 = 0x2000;   // right
    internal const ushort PtrButton3 = 0x4000;   // middle
    internal const ushort PtrDown    = 0x8000;
    internal const ushort PtrWheel   = 0x0200;
    internal const ushort PtrWheelNeg = 0x0100;
}
