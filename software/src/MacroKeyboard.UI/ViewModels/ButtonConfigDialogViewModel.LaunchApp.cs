using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace MacroKeyboard.UI.ViewModels;

public partial class ButtonConfigDialogViewModel
{
    // ── LaunchApp fields ──────────────────────────────────────────────────────

    [ObservableProperty]
    private string _launchAppPath = string.Empty;

    [ObservableProperty]
    private string? _launchAppArguments;

    [ObservableProperty]
    private string? _launchAppWorkingDirectory;

    [ObservableProperty]
    private string? _launchAppIconPath;

    // ── Browse command ────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task BrowseLaunchApp()
    {
        try
        {
            _logger.LogInformation("Browse launch app clicked");

            if (_storageProvider == null)
            {
                _logger.LogWarning("StorageProvider not set");
                return;
            }

            var fileTypes = new FilePickerFileType[]
            {
                new("Executables")
                {
                    Patterns = OperatingSystem.IsWindows()
                        ? new[] { "*.exe", "*.bat", "*.cmd", "*.lnk" }
                        : new[] { "*" },
                }
            };

            var options = new FilePickerOpenOptions
            {
                Title = "Select Application",
                AllowMultiple = false,
                FileTypeFilter = fileTypes
            };

            var result = await _storageProvider.OpenFilePickerAsync(options);

            if (result != null && result.Count > 0)
            {
                LaunchAppPath = result[0].Path.LocalPath;
                _logger.LogInformation("App selected: {Path}", LaunchAppPath);
                await ExtractAndSetAppIconAsync(LaunchAppPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error browsing for application");
        }
    }

    // ── Icon extraction ───────────────────────────────────────────────────────

    internal async Task ExtractAndSetAppIconAsync(string executablePath)
    {
        try
        {
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MacroKeyboard", "icons");
            Directory.CreateDirectory(appDataDir);

            var iconFileName   = Path.GetFileNameWithoutExtension(executablePath) + ".png";
            var iconOutputPath = Path.Combine(appDataDir, iconFileName);

            if (File.Exists(iconOutputPath))
            {
                LaunchAppIconPath = executablePath;
                ImagePath = iconOutputPath;
                _logger.LogDebug("Using cached icon for {Path}", executablePath);
                return;
            }

            if (OperatingSystem.IsWindows())
            {
#pragma warning disable CA1416
                await Task.Run(() => ExtractWindowsAppIcon(executablePath, iconOutputPath));
#pragma warning restore CA1416
                if (File.Exists(iconOutputPath))
                {
                    LaunchAppIconPath = executablePath;
                    ImagePath = iconOutputPath;
                    _logger.LogInformation("App icon extracted to: {Path}", iconOutputPath);
                }
                else
                {
                    LaunchAppIconPath = executablePath;
                    _logger.LogWarning("Icon extraction produced no output for: {Path}", executablePath);
                }
            }
            else
            {
                var desktopIconPath = TryFindLinuxAppIcon(executablePath);
                if (desktopIconPath != null)
                {
                    LaunchAppIconPath = desktopIconPath;
                    ImagePath = desktopIconPath;
                    _logger.LogInformation("Found Linux app icon: {Path}", desktopIconPath);
                }
                else
                {
                    LaunchAppIconPath = null;
                    _logger.LogInformation("No icon found for: {Path}", executablePath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract app icon from {Path}", executablePath);
        }

        await Task.CompletedTask;
    }

    // ── Windows P/Invoke ──────────────────────────────────────────────────────

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void ExtractWindowsAppIcon(string executablePath, string outputPath)
    {
        var hJumbo = TryGetJumboIcon(executablePath);
        if (hJumbo != IntPtr.Zero)
        {
            try
            {
                using var bmp = System.Drawing.Bitmap.FromHicon(hJumbo);
                bmp.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
                return;
            }
            catch { }
            finally { DestroyIcon(hJumbo); }
        }

        var largeIcons = new IntPtr[1];
        var smallIcons = new IntPtr[1];
        try
        {
            ExtractIconEx(executablePath, 0, largeIcons, smallIcons, 1);
            var hIcon = largeIcons[0] != IntPtr.Zero ? largeIcons[0] : smallIcons[0];
            if (hIcon != IntPtr.Zero)
            {
                using var icon   = System.Drawing.Icon.FromHandle(hIcon);
                using var bitmap = icon.ToBitmap();
                bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
                return;
            }
        }
        finally
        {
            if (largeIcons[0] != IntPtr.Zero) DestroyIcon(largeIcons[0]);
            if (smallIcons[0] != IntPtr.Zero) DestroyIcon(smallIcons[0]);
        }

        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath);
            if (icon != null)
            {
                using var bitmap = icon.ToBitmap();
                bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        catch { }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static IntPtr TryGetJumboIcon(string executablePath)
    {
        try
        {
            var shfi = default(SHFILEINFO);
            var res  = SHGetFileInfo(executablePath, 0, ref shfi,
                (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_SYSICONINDEX);
            if (res == IntPtr.Zero) return IntPtr.Zero;

            var iid = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");
            if (SHGetImageList(SHIL_JUMBO, ref iid, out var imageList) != 0 || imageList is null)
                return IntPtr.Zero;

            imageList.GetIcon(shfi.iIcon, ILD_TRANSPARENT, out var hIcon);
            return hIcon;
        }
        catch { return IntPtr.Zero; }
    }

    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("Shell32.dll")]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static extern int SHGetImageList(int iImageList, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellImageList? ppv);

    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex,
        IntPtr[] phiconLarge, IntPtr[] phiconSmall, uint nIcons);

    [DllImport("User32.dll")]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    // Win32 icon API constants (documented values from Shell32 SDK)
    private const uint SHGFI_SYSICONINDEX = 0x4000; // SHGetFileInfo: return system image list index
    private const int  SHIL_JUMBO         = 4;      // SHGetImageList: 256×256 jumbo image list
    private const int  ILD_TRANSPARENT    = 0x1;    // ImageList_Draw: draw transparently

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int    iIcon;
        public uint   dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]  public string szTypeName;
    }

    [ComImport]
    [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellImageList
    {
        [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, out int pi);
        [PreserveSig] int ReplaceIcon(int i, IntPtr hicon, out int pi);
        [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
        [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
        [PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, out int pi);
        [PreserveSig] int Draw(IntPtr pimldp);
        [PreserveSig] int Remove(int i);
        [PreserveSig] int GetIcon(int i, int flags, out IntPtr picon);
    }

    // ── Linux icon search ─────────────────────────────────────────────────────

    // XDG icon theme directories searched in priority order (largest resolution first).
    private static readonly string[] LinuxIconDirs =
    [
        "/usr/share/icons/hicolor/128x128/apps",
        "/usr/share/icons/hicolor/64x64/apps",
        "/usr/share/icons/hicolor/48x48/apps",
        "/usr/share/pixmaps",
    ];

    private static readonly string[] LinuxDesktopDirs =
    [
        "/usr/share/applications",
        "/usr/local/share/applications",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/applications"),
    ];

    private static string? TryFindLinuxAppIcon(string executablePath)
    {
        try
        {
            var appName = Path.GetFileNameWithoutExtension(executablePath).ToLower();

            foreach (var dir in LinuxDesktopDirs)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var desktopFile in Directory.GetFiles(dir, "*.desktop"))
                {
                    var content = File.ReadAllText(desktopFile);
                    if (!content.Contains(executablePath, StringComparison.OrdinalIgnoreCase) &&
                        !content.Contains(appName,        StringComparison.OrdinalIgnoreCase))
                        continue;

                    foreach (var line in content.Split('\n'))
                    {
                        if (!line.StartsWith("Icon=", StringComparison.OrdinalIgnoreCase)) continue;
                        var iconValue = line[5..].Trim();

                        if (Path.IsPathRooted(iconValue) && File.Exists(iconValue))
                            return iconValue;

                        foreach (var iconDir in LinuxIconDirs)
                        {
                            var png = Path.Combine(iconDir, iconValue + ".png");
                            if (File.Exists(png)) return png;
                            var svg = Path.Combine(iconDir, iconValue + ".svg");
                            if (File.Exists(svg)) return svg;
                        }
                    }
                }
            }
        }
        catch { /* ignore errors in icon search */ }

        return null;
    }
}
