using System.Reflection;
using System.Runtime.InteropServices;

namespace MacroKeyboard.Communication.Usb;

/// <summary>
/// Minimal P/Invoke bindings for libusb-1.0.
/// Cross-platform: Linux (libusb-1.0.so.0), macOS (libusb-1.0.dylib), Windows (libusb-1.0.dll).
/// </summary>
internal static class LibUsb
{
    private const string LibName = "usb-1.0";

    // Error codes
    public const int LIBUSB_SUCCESS = 0;
    public const int LIBUSB_ERROR_TIMEOUT = -7;
    public const int LIBUSB_ERROR_NOT_FOUND = -5;
    public const int LIBUSB_ERROR_ACCESS = -3;
    public const int LIBUSB_ERROR_NO_DEVICE = -4;

    // Transfer types
    public const byte LIBUSB_ENDPOINT_IN = 0x80;
    public const byte LIBUSB_ENDPOINT_OUT = 0x00;

    /// <summary>
    /// Register the DLL import resolver to handle platform-specific library names.
    /// Must be called once at startup before any libusb P/Invoke calls.
    /// </summary>
    static LibUsb()
    {
        NativeLibrary.SetDllImportResolver(typeof(LibUsb).Assembly, ResolveLibUsb);
    }

    private static nint ResolveLibUsb(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LibName)
            return nint.Zero;

        // Try platform-specific names
        string[] candidates;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            candidates = new[]
            {
                "libusb-1.0.so.0",      // Installed via apt (no -dev package needed)
                "libusb-1.0.so",         // If -dev package is installed
                "libusb-1.0.so.0.3.0",  // Exact version
            };
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            candidates = new[]
            {
                "libusb-1.0.dylib",
                "libusb-1.0.0.dylib",
                "/opt/homebrew/lib/libusb-1.0.dylib",  // Apple Silicon Homebrew
                "/usr/local/lib/libusb-1.0.dylib",     // Intel Homebrew
            };
        }
        else // Windows
        {
            candidates = new[]
            {
                "libusb-1.0.dll",
                "libusb-1.0",
            };
        }

        foreach (var candidate in candidates)
        {
            if (NativeLibrary.TryLoad(candidate, out nint handle))
                return handle;
        }

        return nint.Zero;
    }

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int libusb_init(out nint ctx);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void libusb_exit(nint ctx);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint libusb_open_device_with_vid_pid(nint ctx, ushort vendorId, ushort productId);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void libusb_close(nint devHandle);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int libusb_claim_interface(nint devHandle, int interfaceNumber);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int libusb_release_interface(nint devHandle, int interfaceNumber);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int libusb_detach_kernel_driver(nint devHandle, int interfaceNumber);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int libusb_kernel_driver_active(nint devHandle, int interfaceNumber);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int libusb_set_auto_detach_kernel_driver(nint devHandle, int enable);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int libusb_bulk_transfer(
        nint devHandle,
        byte endpoint,
        byte[] data,
        int length,
        out int actualLength,
        uint timeout);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint libusb_strerror(int errcode);

    /// <summary>
    /// Get human-readable error string
    /// </summary>
    public static string GetErrorString(int errorCode)
    {
        var ptr = libusb_strerror(errorCode);
        return Marshal.PtrToStringAnsi(ptr) ?? $"Unknown error ({errorCode})";
    }
}
