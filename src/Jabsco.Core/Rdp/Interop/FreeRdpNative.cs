using System.Runtime.InteropServices;

namespace Jabsco.Core.Rdp.Interop;

internal static partial class FreeRdpNative
{
    private const string Lib = "freerdp3";

    // Lifecycle
    [LibraryImport(Lib, EntryPoint = "freerdp_new")]
    internal static partial IntPtr New();

    [LibraryImport(Lib, EntryPoint = "freerdp_free")]
    internal static partial void Free(IntPtr instance);

    [LibraryImport(Lib, EntryPoint = "freerdp_context_new")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ContextNew(IntPtr instance);

    [LibraryImport(Lib, EntryPoint = "freerdp_context_free")]
    internal static partial void ContextFree(IntPtr instance);

    [LibraryImport(Lib, EntryPoint = "freerdp_connect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool Connect(IntPtr instance);

    [LibraryImport(Lib, EntryPoint = "freerdp_disconnect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool Disconnect(IntPtr instance);

    // Takes rdpContext*, not freerdp* — use Marshal.ReadIntPtr(instance, 0) to get context
    [LibraryImport(Lib, EntryPoint = "freerdp_shall_disconnect_context")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShallDisconnectContext(IntPtr context);

    // Takes rdpContext*, not freerdp*
    [LibraryImport(Lib, EntryPoint = "freerdp_check_event_handles")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CheckEventHandles(IntPtr context);

    // Returns rdpSettings* for the instance — platform-correct via C struct access in the shim
    [LibraryImport(Lib, EntryPoint = "jabsco_get_settings")]
    internal static partial IntPtr GetSettings(IntPtr instance);

    // Error reporting — takes rdpContext*, returns static strings (no free needed)
    [LibraryImport(Lib, EntryPoint = "freerdp_get_last_error")]
    internal static partial uint GetLastError(IntPtr context);

    [LibraryImport(Lib, EntryPoint = "freerdp_get_last_error_name")]
    internal static partial IntPtr GetLastErrorName(uint error);

    [LibraryImport(Lib, EntryPoint = "freerdp_get_last_error_string")]
    internal static partial IntPtr GetLastErrorString(uint error);

    internal static string DescribeLastError(IntPtr context)
    {
        var code = GetLastError(context);
        var name = Marshal.PtrToStringAnsi(GetLastErrorName(code)) ?? "unknown";
        var desc = Marshal.PtrToStringAnsi(GetLastErrorString(code)) ?? "";
        return $"0x{code:X8} {name}: {desc}";
    }

    // Settings
    [LibraryImport(Lib, EntryPoint = "freerdp_settings_set_string", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SettingsSetString(IntPtr settings, uint key, string value);

    [LibraryImport(Lib, EntryPoint = "freerdp_settings_set_uint16")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SettingsSetUInt16(IntPtr settings, uint key, ushort value);

    [LibraryImport(Lib, EntryPoint = "freerdp_settings_set_uint32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SettingsSetUInt32(IntPtr settings, uint key, uint value);

    [LibraryImport(Lib, EntryPoint = "freerdp_settings_set_bool")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SettingsSetBool(IntPtr settings, uint key, [MarshalAs(UnmanagedType.Bool)] bool value);

    [LibraryImport(Lib, EntryPoint = "gdi_init")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GdiInit(IntPtr instance, uint format);

    [LibraryImport(Lib, EntryPoint = "gdi_free")]
    internal static partial void GdiFree(IntPtr instance);

    // ── rdpContext struct offsets ─────────────────────────────────────────────
    // freerdp.context    = 0   (first pointer — platform-stable)
    // rdpContext.settings= 320 (Linux x86-64 layout, verified via offsetof)
    // rdpContext.gdi     = 264 (set by gdi_init)
    // rdpContext.input   = 304
    // On Windows the settings pointer is obtained via jabsco_get_settings()
    // instead, because the mingw-w64 struct layout may differ.
    internal const int ContextOffset  = 0;
    internal const int SettingsOffset = 320;
    internal const int GdiOffset      = 264;
    internal const int InputOffset    = 304;

    // ── rdpGdi struct offsets ──────────────────────────────────────────────────
    // rdpGdi.width           = 8   (INT32)
    // rdpGdi.height          = 12  (INT32)
    // rdpGdi.stride          = 16  (UINT32)
    // rdpGdi.cursor_x        = 24  (UINT32)
    // rdpGdi.cursor_y        = 28  (UINT32)
    // rdpGdi.primary_buffer  = 64  (BYTE*)
    internal const int GdiWidthOffset         = 8;
    internal const int GdiHeightOffset        = 12;
    internal const int GdiStrideOffset        = 16;
    internal const int GdiCursorXOffset       = 24;
    internal const int GdiCursorYOffset       = 28;
    internal const int GdiPrimaryBufferOffset = 64;

    // ── Settings key IDs (from freerdp/settings_keys.h, FreeRDP 3.x) ──────────
    // String keys
    internal const uint ServerHostname = 20;
    internal const uint Username       = 21;
    internal const uint Password       = 22;
    internal const uint Domain         = 23;

    // UInt16 keys
    internal const uint ServerPort = 19;

    // UInt32 keys
    internal const uint DesktopWidth  = 129;
    internal const uint DesktopHeight = 130;
    internal const uint ColorDepth    = 131;

    // Bool keys
    internal const uint NlaSecurity             = 1089;
    internal const uint IgnoreCertificate       = 1408;
    internal const uint AutoAcceptCertificate   = 1419;
    internal const uint NegotiateSecurityLayer  = 1096;
    internal const uint VmConnectMode           = 1102;
    internal const uint SendPreconnectionPdu    = 1156;

    // String key: PreconnectionBlob (VM GUID for Hyper-V vmconnect)
    internal const uint PreconnectionBlob       = 1155;

    // ── GDI pixel format: PIXEL_FORMAT_BGRA32 = 0x20048888 ───────────────────
    internal const uint PixelFormatBgra32 = 0x20048888;
}
