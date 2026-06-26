using MacroKeyboard.Backend;
using MacroKeyboard.Core.Models;
using MacroKeyboard.Core.Services;
using MacroKeyboard.Infrastructure.Services;
using MacroKeyboard.Shared.Plugin;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;

namespace MacroKeyboard.Backend.Plugin;

// ── Plugin instance base ──────────────────────────────────────────────────────

public abstract class PluginInstance : IDisposable
{
    protected readonly PluginManifest Manifest;
    protected readonly string PluginDirectory;
    protected readonly ILogger Logger;
    protected bool IsRunning;

    protected PluginInstance(PluginManifest manifest, string pluginDirectory, ILogger logger)
    {
        Manifest        = manifest;
        PluginDirectory = pluginDirectory;
        Logger          = logger;
    }

    public abstract Task StartAsync(CancellationToken cancellationToken = default);
    public abstract Task StopAsync(CancellationToken cancellationToken = default);
    public abstract void Dispose();
}

// ── Executable plugin (Node.js, Python, etc.) ─────────────────────────────────

public class ExecutablePluginInstance : PluginInstance
{
    private System.Diagnostics.Process? _process;

    public ExecutablePluginInstance(PluginManifest manifest, string pluginDirectory, ILogger logger)
        : base(manifest, pluginDirectory, logger) { }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            Logger.LogWarning("Plugin {Id} is already running", Manifest.Id);
            return;
        }

        var entryPoint = Manifest.EffectiveEntryPoint;
        if (string.IsNullOrEmpty(entryPoint))
            throw new InvalidOperationException($"No entry point for plugin {Manifest.Id}");

        var entryPointPath = Path.Combine(PluginDirectory, entryPoint);

        if (!File.Exists(entryPointPath))
            throw new FileNotFoundException($"Plugin entry point not found: {entryPointPath}");

        // Build Stream Deck -info JSON
        var infoJson = JsonConvert.SerializeObject(new
        {
            application = new
            {
                font            = "Arial",
                language        = "en",
                platform        = "windows",
                platformVersion = "10.0.0",
                version         = "1.0.0"
            },
            plugin         = new { uuid = Manifest.Id, version = Manifest.Version },
            devicePixelRatio = 1,
            colors           = new { },
            devices          = new[]
            {
                new
                {
                    id   = "MK_DEVICE_0",
                    name = "MacroKeyboard",
                    size = new { columns = DeviceConstants.Columns, rows = DeviceConstants.Rows },
                    type = 0
                }
            }
        });

        // HTML plugins are designed to run inside Elgato's built-in Chromium WebView.
        // We serve them through PropertyInspectorServer (port 8787) and launch a
        // headless Edge/Chrome process — no visible window, runs in background.
        if (Path.GetExtension(entryPointPath).Equals(".html", StringComparison.OrdinalIgnoreCase))
        {
            var entryFile = Path.GetFileName(entryPointPath);
            var httpUrl = string.Concat(
                $"http://localhost:{PropertyInspectorServer.HttpPort}/plugins/",
                Uri.EscapeDataString(Manifest.Id), "/", entryFile,
                $"?port={WebSocketServer.Port}",
                "&pluginUUID=",    Uri.EscapeDataString(Manifest.Id),
                "&registerEvent=", Uri.EscapeDataString("registerPlugin"),
                "&info=",          Uri.EscapeDataString(infoJson));

            var browserExe = FindChromiumExe();
            if (browserExe != null)
            {
                Logger.LogInformation("[{Id}] HTML plugin — launching headless Chromium: {Exe}", Manifest.Id, browserExe);

                // Each plugin gets its own isolated profile dir so multiple plugins
                // can run simultaneously without profile-lock conflicts.
                // Wipe the dir before each start to remove stale SingletonLock files
                // left by a previous crash or un-graceful shutdown.
                var userDataDir = Path.Combine(Path.GetTempPath(), "mk-plugins", Manifest.Id);
                if (Directory.Exists(userDataDir))
                    Directory.Delete(userDataDir, recursive: true);
                Directory.CreateDirectory(userDataDir);

                var hInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName               = browserExe,
                    WorkingDirectory       = PluginDirectory,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError  = true,   // capture JS console errors
                };
                hInfo.ArgumentList.Add("--headless=new");
                hInfo.ArgumentList.Add("--disable-gpu");
                hInfo.ArgumentList.Add("--no-sandbox");
                hInfo.ArgumentList.Add("--no-first-run");
                hInfo.ArgumentList.Add("--no-default-browser-check");
                hInfo.ArgumentList.Add("--disable-extensions");
                hInfo.ArgumentList.Add("--disable-background-mode");
                hInfo.ArgumentList.Add($"--user-data-dir={userDataDir}");
                hInfo.ArgumentList.Add(httpUrl);

                _process = new System.Diagnostics.Process
                {
                    StartInfo           = hInfo,
                    EnableRaisingEvents = true
                };
                _process.Exited += (_, _) =>
                {
                    IsRunning = false;
                    Logger.LogInformation("[{Id}] Headless browser process exited", Manifest.Id);
                };

                try
                {
                    _process.Start();
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "[{Id}] Failed to start headless browser: {Exe}", Manifest.Id, browserExe);
                    throw;
                }

                // Drain stderr in background so the pipe buffer never blocks the browser.
                // Lines are forwarded to the backend log for diagnosing JS/WS errors.
                var pluginId = Manifest.Id;
                _ = Task.Run(async () =>
                {
                    string? line;
                    while ((line = await _process.StandardError.ReadLineAsync()) != null)
                        if (line.Length > 0)
                            Logger.LogDebug("[{Id}:browser] {Line}", pluginId, line);
                });

                IsRunning = true;
                Logger.LogInformation("[{Id}] HTML plugin running headlessly (PID {Pid})", Manifest.Id, _process.Id);
            }
            else
            {
                // No Chromium found — fall back to a visible browser tab with a clear warning.
                Logger.LogWarning(
                    "[{Id}] No Edge/Chrome installation found for headless execution. " +
                    "Opening plugin.html in the default browser instead — plugin will stop working when the tab is closed. " +
                    "Install Microsoft Edge or Google Chrome to run HTML plugins in the background.",
                    Manifest.Id);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = httpUrl,
                    UseShellExecute = true
                });

                IsRunning = true;
            }
            return;
        }

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            WorkingDirectory       = PluginDirectory,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true
        };

        if (string.IsNullOrEmpty(Manifest.Runtime))
        {
            startInfo.FileName = entryPointPath;
        }
        else
        {
            // Interpreted runtime (node, python3, …)
            startInfo.FileName = Manifest.Runtime;
            startInfo.ArgumentList.Add(entryPointPath);
        }

        // Stream Deck standard CLI args
        startInfo.ArgumentList.Add("-port");
        startInfo.ArgumentList.Add($"{WebSocketServer.Port}");
        startInfo.ArgumentList.Add("-pluginUUID");
        startInfo.ArgumentList.Add(Manifest.Id);
        startInfo.ArgumentList.Add("-registerEvent");
        startInfo.ArgumentList.Add("registerPlugin");
        startInfo.ArgumentList.Add("-info");
        startInfo.ArgumentList.Add(infoJson);

        Logger.LogInformation("[{Id}] Launching: {Exe} -port {Port} -pluginUUID {Uuid} -registerEvent registerPlugin -info <json>",
            Manifest.Id, startInfo.FileName, WebSocketServer.Port, Manifest.Id);

        _process = new System.Diagnostics.Process
        {
            StartInfo            = startInfo,
            EnableRaisingEvents  = true
        };

        _process.Exited += (_, _) =>
        {
            var code = -1;
            try { code = _process?.ExitCode ?? -1; } catch { }
            IsRunning = false;
            if (code == 0)
                Logger.LogInformation("[{Id}] Process exited cleanly (code 0)", Manifest.Id);
            else
                Logger.LogError("[{Id}] Process exited unexpectedly with code {Code}", Manifest.Id, code);
        };

        try
        {
            _process.Start();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{Id}] Failed to start process: {Exe}", Manifest.Id, startInfo.FileName);
            throw;
        }

        // Task-based stream reading captures output even when the process exits immediately.
        // BeginOutputReadLine callbacks may be too late when the process lives < 300ms.
        var stdoutTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await _process.StandardOutput.ReadLineAsync()) != null)
                if (line.Length > 0)
                    Logger.LogInformation("[{Id}:stdout] {Out}", Manifest.Id, line);
        });
        var stderrTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await _process.StandardError.ReadLineAsync()) != null)
                if (line.Length > 0)
                    Logger.LogWarning("[{Id}:stderr] {Err}", Manifest.Id, line);
        });

        // Brief pause to detect an immediate crash (plugins that refuse to start exit in < 500ms)
        await Task.Delay(600, cancellationToken);
        if (_process.HasExited)
        {
            // Drain any buffered output before logging the failure
            await Task.WhenAny(Task.WhenAll(stdoutTask, stderrTask), Task.Delay(500));
            Logger.LogError("[{Id}] Process exited immediately after launch (code {Code}). Check that the plugin binary is valid and all dependencies are present.",
                Manifest.Id, _process.ExitCode);
            IsRunning = false;
            return;
        }

        IsRunning = true;
        Logger.LogInformation("[{Id}] Plugin process running (PID {Pid})", Manifest.Id, _process.Id);
    }

    public override async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning || _process == null) return;
        try
        {
            if (!_process.HasExited)
            {
                // Kill entire process tree — Chrome/Edge spawns renderer/GPU child
                // processes that would otherwise become orphans on Windows.
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync(cancellationToken);
            }
        }
        catch (Exception ex) { Logger.LogError(ex, "Error stopping plugin {Id}", Manifest.Id); }
        finally
        {
            IsRunning = false;
            _process?.Dispose();
            _process = null;
        }
    }

    public override void Dispose() => StopAsync().GetAwaiter().GetResult();

    private static string? FindChromiumExe()
    {
        // Check common Edge and Chrome install paths on Windows.
        // On non-Windows we rely on PATH entries (edge / google-chrome).
        var candidates = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            var programFiles  = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var localAppData  = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            candidates.AddRange(new[]
            {
                // Edge (usually pre-installed on Windows 10/11)
                Path.Combine(programFiles,  "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(programFilesX, "Microsoft", "Edge", "Application", "msedge.exe"),
                // Chrome stable
                Path.Combine(programFiles,  "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(programFilesX, "Google", "Chrome", "Application", "chrome.exe"),
                // Chrome per-user install
                Path.Combine(localAppData,  "Google", "Chrome", "Application", "chrome.exe"),
            });
        }
        else if (OperatingSystem.IsMacOS())
        {
            candidates.AddRange(new[]
            {
                "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
                "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
            });
        }
        else
        {
            // Linux: look for executables on PATH
            candidates.AddRange(new[] { "microsoft-edge", "google-chrome", "google-chrome-stable", "chromium-browser", "chromium" });
        }

        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return path;

            // For PATH-based entries (Linux) check via `which`-equivalent
            if (!path.Contains(Path.DirectorySeparatorChar))
            {
                try
                {
                    var result = System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo("which", path)
                        {
                            RedirectStandardOutput = true,
                            UseShellExecute        = false,
                            CreateNoWindow         = true
                        });
                    var resolved = result?.StandardOutput.ReadToEnd().Trim();
                    if (!string.IsNullOrEmpty(resolved) && File.Exists(resolved))
                        return resolved;
                }
                catch { /* 'which' is not available on this platform — skip candidate */ }
            }
        }

        return null;
    }
}

// ── Managed plugin (.NET DLL) ─────────────────────────────────────────────────

public class ManagedPluginInstance : PluginInstance
{
    private AssemblyLoadContext? _loadContext;
    private IPlugin?             _pluginInstance;
    private readonly IDeviceService _deviceService;

    public ManagedPluginInstance(PluginManifest manifest, string pluginDirectory,
        ILogger logger, IDeviceService deviceService)
        : base(manifest, pluginDirectory, logger)
    {
        _deviceService = deviceService;
    }

    public Task OnButtonPressedAsync(string actionId, string? settings, int buttonIndex, CancellationToken ct)
        => _pluginInstance?.OnButtonPressedAsync(actionId, settings, buttonIndex, ct) ?? Task.CompletedTask;

    public Task OnButtonReleasedAsync(string actionId, string? settings, int buttonIndex, CancellationToken ct)
        => _pluginInstance?.OnButtonReleasedAsync(actionId, settings, buttonIndex, ct) ?? Task.CompletedTask;

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) return;

        var assemblyPath = Path.Combine(PluginDirectory, Manifest.EntryPoint
            ?? throw new InvalidOperationException($"No EntryPoint for managed plugin {Manifest.Id}"));

        if (!File.Exists(assemblyPath))
            throw new FileNotFoundException($"Assembly not found: {assemblyPath}");

        Logger.LogInformation("Loading managed plugin: {Path}", assemblyPath);

        _loadContext = new AssemblyLoadContext($"Plugin_{Manifest.Id}", isCollectible: true);
        var assembly = _loadContext.LoadFromAssemblyPath(assemblyPath);

        var pluginType = assembly.GetTypes()
            .FirstOrDefault(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            ?? throw new InvalidOperationException($"No IPlugin implementation in {assemblyPath}");

        _pluginInstance = Activator.CreateInstance(pluginType) as IPlugin
            ?? throw new InvalidOperationException($"Cannot create {pluginType.FullName}");

        var context = new PluginContext(Manifest.Id, Logger, _deviceService);
        await _pluginInstance.InitializeAsync(context, cancellationToken);
        await _pluginInstance.StartAsync(cancellationToken);

        IsRunning = true;
        Logger.LogInformation("Started managed plugin: {Id}", Manifest.Id);
    }

    public override async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning || _pluginInstance == null) return;
        try
        {
            await _pluginInstance.StopAsync(cancellationToken);
            _pluginInstance.Dispose();
            _pluginInstance = null;
            _loadContext?.Unload();
            _loadContext = null;
            IsRunning = false;
            Logger.LogInformation("Stopped managed plugin: {Id}", Manifest.Id);
        }
        catch (Exception ex) { Logger.LogError(ex, "Error stopping managed plugin {Id}", Manifest.Id); }
    }

    public override void Dispose() => StopAsync().GetAwaiter().GetResult();
}

// ── Plugin context (managed plugin ↔ device bridge) ──────────────────────────

// ── Manifest JSON helper ──────────────────────────────────────────────────────

/// <summary>
/// Deserializes a JSON field that may be either a plain string or an array of strings
/// into a string[]. Handles real-world SD manifests where "Category" is a single string.
/// </summary>
internal sealed class StringOrArrayConverter : JsonConverter<string[]>
{
    public override string[]? ReadJson(JsonReader reader, Type objectType,
        string[]? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.StartArray)
        {
            // Read manually — calling serializer.Deserialize<string[]> here would re-enter
            // this converter and cause a stack overflow.
            var items = new List<string>();
            while (reader.Read() && reader.TokenType != JsonToken.EndArray)
                if (reader.Value?.ToString() is { } s)
                    items.Add(s);
            return items.ToArray();
        }

        if (reader.TokenType == JsonToken.Null)
            return Array.Empty<string>();

        var single = reader.Value?.ToString();
        return single != null ? new[] { single } : Array.Empty<string>();
    }

    public override void WriteJson(JsonWriter writer, string[]? value, JsonSerializer serializer)
        => serializer.Serialize(writer, value);
}

internal class PluginContext : IPluginContext
{
    private readonly ILogger _logger;
    private readonly IDeviceService _deviceService;

    public string PluginId { get; }

    public PluginContext(string pluginId, ILogger logger, IDeviceService deviceService)
    {
        PluginId       = pluginId;
        _logger        = logger;
        _deviceService = deviceService;
    }

    public Task SetButtonImageAsync(int buttonIndex, byte[] imageData, CancellationToken ct = default)
        => _deviceService.SendButtonImageAsync(0, (byte)buttonIndex, imageData, null, ct);

    public Task SetButtonTitleAsync(int buttonIndex, string title, CancellationToken ct = default)
        => _deviceService.SetButtonNameAsync(0, (byte)buttonIndex, title, ct);

    public Task SetLedColorAsync(int buttonIndex, byte r, byte g, byte b, CancellationToken ct = default)
        => _deviceService.SetLedColorAsync(0, (byte)buttonIndex, new LedConfig { R = r, G = g, B = b, Brightness = 100 }, ct);

    public async Task ShowAlertAsync(int buttonIndex, CancellationToken ct = default)
    {
        await _deviceService.SetLedColorAsync(0, (byte)buttonIndex, new LedConfig { R = 255, G = 0, B = 0, Brightness = 100 }, ct);
        await Task.Delay(200, ct);
        await _deviceService.SetLedColorAsync(0, (byte)buttonIndex, new LedConfig { R = 0, G = 0, B = 0, Brightness = 0 }, ct);
    }

    public void LogInfo(string message)    => _logger.LogInformation("[{Id}] {Msg}", PluginId, message);
    public void LogWarning(string message) => _logger.LogWarning("[{Id}] {Msg}", PluginId, message);
    public void LogError(string message, Exception? ex = null)
        => _logger.LogError(ex, "[{Id}] {Msg}", PluginId, message);

    public async Task<T?> GetSettingsAsync<T>(CancellationToken ct = default) where T : class
    {
        var path = SettingsPath();
        if (!File.Exists(path)) return null;
        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            return JsonConvert.DeserializeObject<T>(json);
        }
        catch (Exception ex) { _logger.LogError(ex, "[{Id}] Failed to load settings", PluginId); return null; }
    }

    public async Task SaveSettingsAsync<T>(T settings, CancellationToken ct = default) where T : class
    {
        var path = SettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonConvert.SerializeObject(settings, Formatting.Indented), ct);
    }

    private string SettingsPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MacroKeyboard", "plugins", PluginId, "settings.json");
}
