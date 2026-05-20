using LibUsbDotNet;
using LibUsbDotNet.LibUsb;
using LibUsbDotNet.Main;

namespace MacroKeyboard.Communication.Usb;

/// <summary>
/// Cross-platform USB device wrapper using LibUsbDotNet v3.
/// Works on Windows (WinUSB via bundled libusb-1.0.dll), Linux (system libusb), macOS (bundled libusb).
///
/// LibUsbDotNet NuGet package bundles native libusb-1.0 libraries for Windows and macOS,
/// eliminating the need to manually install libusb-1.0.dll.
/// On Windows, the device must have a WinUSB-compatible driver installed
/// (via Zadig or a custom INF file).
/// </summary>
internal sealed class UsbDeviceWrapper : IDisposable
{
    private UsbContext? _context;
    private IUsbDevice? _device;
    private UsbEndpointReader? _reader;
    private UsbEndpointWriter? _writer;
    private bool _disposed;

    // Error codes (compatible with old LibUsb constants used in HidDeviceManager)
    public const int SUCCESS = 0;
    public const int ERROR_TIMEOUT = -7;
    public const int ERROR_NOT_FOUND = -5;
    public const int ERROR_ACCESS = -3;
    public const int ERROR_NO_DEVICE = -4;
    public const int ERROR_IO = -1;

    public bool IsOpen => _device != null && _device.IsOpen;

    /// <summary>
    /// Open a USB device by VID/PID.
    /// Returns true if the device was found and opened successfully.
    /// </summary>
    public bool Open(int vendorId, int productId)
    {
        _context = new UsbContext();

        var finder = new UsbDeviceFinder(vendorId, productId);
        _device = _context.Find(finder);

        if (_device == null)
        {
            _context.Dispose();
            _context = null;
            return false;
        }

        _device.Open();

        // Set configuration
        _device.SetConfiguration(1);

        return true;
    }

    /// <summary>
    /// Claim a USB interface and open bulk endpoints.
    /// </summary>
    public int ClaimInterface(int interfaceNumber, byte epOut, byte epIn)
    {
        if (_device == null)
            return ERROR_NO_DEVICE;

        try
        {
            if (!_device.ClaimInterface(interfaceNumber))
                return ERROR_ACCESS;

            // Open endpoints
            _reader = _device.OpenEndpointReader((ReadEndpointID)epIn);
            _writer = _device.OpenEndpointWriter((WriteEndpointID)epOut);

            if (_reader == null || _writer == null)
                return ERROR_IO;

            return SUCCESS;
        }
        catch (UsbException ex)
        {
            return MapError(ex.ErrorCode);
        }
        catch (Exception)
        {
            return ERROR_IO;
        }
    }

    /// <summary>
    /// Write data to the bulk OUT endpoint.
    /// </summary>
    public int BulkWrite(byte[] data, int length, out int bytesWritten, int timeoutMs)
    {
        bytesWritten = 0;

        if (_writer == null || _device == null || !_device.IsOpen)
            return ERROR_NO_DEVICE;

        try
        {
            var ec = _writer.Write(data, 0, length, timeoutMs, out bytesWritten);
            return MapError(ec);
        }
        catch (ObjectDisposedException)
        {
            return ERROR_NO_DEVICE;
        }
        catch (UsbException ex)
        {
            return MapError(ex.ErrorCode);
        }
        catch (Exception)
        {
            return ERROR_IO;
        }
    }

    /// <summary>
    /// Read data from the bulk IN endpoint.
    /// </summary>
    public int BulkRead(byte[] buffer, int length, out int bytesRead, int timeoutMs)
    {
        bytesRead = 0;

        if (_reader == null || _device == null || !_device.IsOpen)
            return ERROR_NO_DEVICE;

        try
        {
            var ec = _reader.Read(buffer, 0, length, timeoutMs, out bytesRead);
            return MapError(ec);
        }
        catch (ObjectDisposedException)
        {
            return ERROR_NO_DEVICE;
        }
        catch (UsbException ex)
        {
            return MapError(ex.ErrorCode);
        }
        catch (Exception)
        {
            return ERROR_IO;
        }
    }

    /// <summary>
    /// Release the claimed interface.
    /// </summary>
    public void ReleaseInterface(int interfaceNumber)
    {
        try
        {
            _device?.ReleaseInterface(interfaceNumber);
        }
        catch
        {
            // Ignore errors during cleanup
        }
    }

    /// <summary>
    /// Close the device and release all resources.
    /// </summary>
    public void Close()
    {
        // UsbEndpointReader/Writer are not IDisposable in LibUsbDotNet v3;
        // they are cleaned up when the device is closed/disposed.
        _reader = null;
        _writer = null;

        try
        {
            if (_device != null)
            {
                if (_device.IsOpen)
                    _device.Close();
                _device.Dispose();
            }
        }
        catch
        {
            // Ignore
        }
        finally
        {
            _device = null;
        }

        try
        {
            _context?.Dispose();
        }
        catch
        {
            // Ignore
        }
        finally
        {
            _context = null;
        }
    }

    /// <summary>
    /// Map LibUsbDotNet Error enum to our integer error codes.
    /// </summary>
    private static int MapError(Error error)
    {
        return error switch
        {
            Error.Success => SUCCESS,
            Error.Timeout => ERROR_TIMEOUT,
            Error.NotFound => ERROR_NOT_FOUND,
            Error.Access => ERROR_ACCESS,
            Error.NoDevice => ERROR_NO_DEVICE,
            Error.Io => ERROR_IO,
            _ => ERROR_IO
        };
    }

    /// <summary>
    /// Get a human-readable error string for an error code.
    /// </summary>
    public static string GetErrorString(int errorCode)
    {
        return errorCode switch
        {
            SUCCESS => "Success",
            ERROR_TIMEOUT => "Transfer timed out",
            ERROR_NOT_FOUND => "Entity not found",
            ERROR_ACCESS => "Access denied (insufficient permissions)",
            ERROR_NO_DEVICE => "No such device (it may have been disconnected)",
            ERROR_IO => "Input/output error",
            _ => $"Unknown error ({errorCode})"
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
    }
}
