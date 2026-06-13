using MacroKeyboard.Core.Models;
using MacroKeyboard.Core.Services;
using MacroKeyboard.Shared.Plugin;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;

namespace MacroKeyboard.Backend.Plugin;

/// <summary>
/// Manages plugin lifecycle and registration
/// </summary>
public class PluginManager : IDisposable
{
    private readonly ILogger<PluginManager> _logger;
    private readonly string _pluginsDirectory;
    private readonly IDeviceService _deviceService;
    private readonly WebSocketServer _webSocketServer;
    private readonly ConcurrentDictionary<string, PluginInstance> _plugins = new();
    private readonly ConcurrentDictionary<string, PluginManifest> _manifests = new();

    public PluginManager(
        ILogger<PluginManager> logger,
        IDeviceService deviceService,
        WebSocketServer webSocketServer,
        string pluginsDirectory)
    {
        _logger = logger;
        _deviceService = deviceService;
        _webSocketServer = webSocketServer;
        _pluginsDirectory = pluginsDirectory;
    }

    /// <summary>
    /// Discover and load all plugins from the plugins directory
    /// </summary>
    public async Task LoadPluginsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Loading plugins from {PluginsDirectory}", _pluginsDirectory);

        if (!Directory.Exists(_pluginsDirectory))
        {
            _logger.LogWarning("Plugins directory does not exist: {PluginsDirectory}", _pluginsDirectory);
            Directory.CreateDirectory(_pluginsDirectory);
            return;
        }

        var pluginDirs = Directory.GetDirectories(_pluginsDirectory);
        
        foreach (var pluginDir in pluginDirs)
        {
            try
            {
                await LoadPluginAsync(pluginDir, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load plugin from {PluginDir}", pluginDir);
            }
        }

        _logger.LogInformation("Loaded {Count} plugins", _plugins.Count);
    }

    /// <summary>
    /// Load a single plugin from directory
    /// </summary>
    private async Task LoadPluginAsync(string pluginDir, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(pluginDir, "manifest.json");
        
        if (!File.Exists(manifestPath))
        {
            _logger.LogWarning("Plugin manifest not found: {ManifestPath}", manifestPath);
            return;
        }

        var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
        var manifest = JsonConvert.DeserializeObject<PluginManifest>(manifestJson);

        if (manifest == null)
        {
            _logger.LogWarning("Failed to parse plugin manifest: {ManifestPath}", manifestPath);
            return;
        }

        _logger.LogInformation("Loading plugin: {PluginName} v{Version} by {Author}", 
            manifest.Name, manifest.Version, manifest.Author);

        _manifests[manifest.Id] = manifest;

        // Create plugin instance based on type
        PluginInstance? instance = manifest.Type switch
        {
            "executable" => new ExecutablePluginInstance(manifest, pluginDir, _logger),
            "managed" => new ManagedPluginInstance(manifest, pluginDir, _logger, _deviceService),
            _ => null
        };

        if (instance != null)
        {
            _plugins[manifest.Id] = instance;
            _logger.LogInformation("Plugin loaded: {PluginId}", manifest.Id);
        }
        else
        {
            _logger.LogWarning("Unsupported plugin type: {Type}", manifest.Type);
        }
    }

    /// <summary>
    /// Start a plugin
    /// </summary>
    public async Task StartPluginAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        if (!_plugins.TryGetValue(pluginId, out var instance))
        {
            throw new InvalidOperationException($"Plugin not found: {pluginId}");
        }

        await instance.StartAsync(cancellationToken);
        _logger.LogInformation("Plugin started: {PluginId}", pluginId);
    }

    /// <summary>
    /// Stop a plugin
    /// </summary>
    public async Task StopPluginAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        if (!_plugins.TryGetValue(pluginId, out var instance))
        {
            throw new InvalidOperationException($"Plugin not found: {pluginId}");
        }

        await instance.StopAsync(cancellationToken);
        _logger.LogInformation("Plugin stopped: {PluginId}", pluginId);
    }

    /// <summary>
    /// Get all loaded plugin manifests
    /// </summary>
    public IEnumerable<PluginManifest> GetPlugins()
    {
        return _manifests.Values;
    }

    /// <summary>
    /// Get plugin by ID
    /// </summary>
    public PluginManifest? GetPlugin(string pluginId)
    {
        return _manifests.TryGetValue(pluginId, out var manifest) ? manifest : null;
    }

    /// <summary>
    /// Dispatch a button press to the appropriate plugin instance.
    /// Managed: calls IPlugin.OnButtonPressedAsync directly.
    /// Executable: sends a Stream Deck-compatible keyDown event via WebSocket.
    /// </summary>
    public async Task DispatchButtonPressAsync(
        string pluginId, string actionId, string? settings, int buttonIndex,
        CancellationToken ct = default)
    {
        if (!_plugins.TryGetValue(pluginId, out var instance))
        {
            _logger.LogWarning("DispatchButtonPress: plugin not found: {PluginId}", pluginId);
            return;
        }

        if (instance is ManagedPluginInstance managed)
        {
            await managed.OnButtonPressedAsync(actionId, settings, buttonIndex, ct);
        }
        else
        {
            await _webSocketServer.BroadcastAsync(new PluginMessage
            {
                Event = "keyDown",
                Action = actionId,
                Context = $"{pluginId}:{buttonIndex}",
                Payload = new
                {
                    settings = string.IsNullOrEmpty(settings) ? null : JsonConvert.DeserializeObject(settings),
                    coordinates = new { column = buttonIndex % 5, row = buttonIndex / 5 },
                    isInMultiAction = false
                }
            }, ct);
        }
    }

    /// <summary>
    /// Dispatch a button release to the appropriate plugin instance.
    /// </summary>
    public async Task DispatchButtonReleaseAsync(
        string pluginId, string actionId, string? settings, int buttonIndex,
        CancellationToken ct = default)
    {
        if (!_plugins.TryGetValue(pluginId, out var instance))
        {
            _logger.LogWarning("DispatchButtonRelease: plugin not found: {PluginId}", pluginId);
            return;
        }

        if (instance is ManagedPluginInstance managed)
        {
            await managed.OnButtonReleasedAsync(actionId, settings, buttonIndex, ct);
        }
        else
        {
            await _webSocketServer.BroadcastAsync(new PluginMessage
            {
                Event = "keyUp",
                Action = actionId,
                Context = $"{pluginId}:{buttonIndex}",
                Payload = new
                {
                    settings = string.IsNullOrEmpty(settings) ? null : JsonConvert.DeserializeObject(settings),
                    coordinates = new { column = buttonIndex % 5, row = buttonIndex / 5 },
                    isInMultiAction = false
                }
            }, ct);
        }
    }

    /// <summary>
    /// Returns all available plugin actions — used by the UI to populate the action palette.
    /// </summary>
    public IEnumerable<(string PluginId, string PluginName, PluginAction Action)> GetLoadedActions()
    {
        foreach (var manifest in _manifests.Values)
        {
            foreach (var action in manifest.Actions)
                yield return (manifest.Id, manifest.Name, action);
        }
    }

    public void Dispose()
    {
        foreach (var instance in _plugins.Values)
        {
            instance.Dispose();
        }
        _plugins.Clear();
    }
}

/// <summary>
/// Base class for plugin instances
/// </summary>
public abstract class PluginInstance : IDisposable
{
    protected readonly PluginManifest Manifest;
    protected readonly string PluginDirectory;
    protected readonly ILogger Logger;
    protected bool IsRunning;

    protected PluginInstance(PluginManifest manifest, string pluginDirectory, ILogger logger)
    {
        Manifest = manifest;
        PluginDirectory = pluginDirectory;
        Logger = logger;
    }

    public abstract Task StartAsync(CancellationToken cancellationToken = default);
    public abstract Task StopAsync(CancellationToken cancellationToken = default);
    public abstract void Dispose();
}

/// <summary>
/// Plugin instance for executable plugins (Node.js, Python, etc.)
/// </summary>
public class ExecutablePluginInstance : PluginInstance
{
    private System.Diagnostics.Process? _process;

    public ExecutablePluginInstance(PluginManifest manifest, string pluginDirectory, ILogger logger)
        : base(manifest, pluginDirectory, logger)
    {
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            Logger.LogWarning("Plugin {PluginId} is already running", Manifest.Id);
            return;
        }

        if (string.IsNullOrEmpty(Manifest.EntryPoint) || string.IsNullOrEmpty(Manifest.Runtime))
        {
            throw new InvalidOperationException("EntryPoint and Runtime must be specified for executable plugins");
        }

        var entryPointPath = Path.Combine(PluginDirectory, Manifest.EntryPoint);
        
        _process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Manifest.Runtime,
                Arguments = entryPointPath,
                WorkingDirectory = PluginDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        _process.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                Logger.LogInformation("[{PluginId}] {Output}", Manifest.Id, e.Data);
            }
        };

        _process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                Logger.LogError("[{PluginId}] {Error}", Manifest.Id, e.Data);
            }
        };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        IsRunning = true;
        Logger.LogInformation("Started executable plugin: {PluginId}", Manifest.Id);

        await Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning || _process == null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill();
                await _process.WaitForExitAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error stopping plugin {PluginId}", Manifest.Id);
        }
        finally
        {
            IsRunning = false;
            _process?.Dispose();
            _process = null;
        }
    }

    public override void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }
}

/// <summary>
/// Plugin instance for managed (.NET) plugins
/// </summary>
public class ManagedPluginInstance : PluginInstance
{
    private AssemblyLoadContext? _loadContext;
    private IPlugin? _pluginInstance;
    private Assembly? _pluginAssembly;
    private readonly IDeviceService _deviceService;

    public ManagedPluginInstance(PluginManifest manifest, string pluginDirectory, ILogger logger, IDeviceService deviceService)
        : base(manifest, pluginDirectory, logger)
    {
        _deviceService = deviceService;
    }

    public Task OnButtonPressedAsync(string actionId, string? settings, int buttonIndex, CancellationToken ct = default)
        => _pluginInstance?.OnButtonPressedAsync(actionId, settings, buttonIndex, ct) ?? Task.CompletedTask;

    public Task OnButtonReleasedAsync(string actionId, string? settings, int buttonIndex, CancellationToken ct = default)
        => _pluginInstance?.OnButtonReleasedAsync(actionId, settings, buttonIndex, ct) ?? Task.CompletedTask;

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            Logger.LogWarning("Plugin {PluginId} is already running", Manifest.Id);
            return;
        }

        if (string.IsNullOrEmpty(Manifest.EntryPoint))
        {
            throw new InvalidOperationException("EntryPoint must be specified for managed plugins");
        }

        try
        {
            var assemblyPath = Path.Combine(PluginDirectory, Manifest.EntryPoint);
            
            if (!File.Exists(assemblyPath))
            {
                throw new FileNotFoundException($"Plugin assembly not found: {assemblyPath}");
            }

            Logger.LogInformation("Loading managed plugin from: {AssemblyPath}", assemblyPath);

            // Create isolated AssemblyLoadContext for plugin
            _loadContext = new AssemblyLoadContext($"Plugin_{Manifest.Id}", isCollectible: true);
            
            // Load the plugin assembly
            _pluginAssembly = _loadContext.LoadFromAssemblyPath(assemblyPath);
            
            // Find types that implement IPlugin
            var pluginType = _pluginAssembly.GetTypes()
                .FirstOrDefault(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            if (pluginType == null)
            {
                throw new InvalidOperationException($"No type implementing IPlugin found in assembly: {assemblyPath}");
            }

            Logger.LogInformation("Found plugin type: {PluginType}", pluginType.FullName);

            // Create plugin instance
            _pluginInstance = Activator.CreateInstance(pluginType) as IPlugin;
            
            if (_pluginInstance == null)
            {
                throw new InvalidOperationException($"Failed to create instance of plugin type: {pluginType.FullName}");
            }

            var context = new PluginContext(Manifest.Id, Logger, _deviceService);
            
            // Initialize and start the plugin
            await _pluginInstance.InitializeAsync(context, cancellationToken);
            await _pluginInstance.StartAsync(cancellationToken);

            IsRunning = true;
            Logger.LogInformation("Started managed plugin: {PluginId}", Manifest.Id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error starting managed plugin {PluginId}", Manifest.Id);
            
            // Cleanup on error
            _pluginInstance?.Dispose();
            _pluginInstance = null;
            _loadContext?.Unload();
            _loadContext = null;
            
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning || _pluginInstance == null)
        {
            return;
        }

        try
        {
            Logger.LogInformation("Stopping managed plugin: {PluginId}", Manifest.Id);
            
            await _pluginInstance.StopAsync(cancellationToken);
            _pluginInstance.Dispose();
            _pluginInstance = null;

            // Unload the plugin assembly context
            _loadContext?.Unload();
            _loadContext = null;
            _pluginAssembly = null;

            IsRunning = false;
            Logger.LogInformation("Stopped managed plugin: {PluginId}", Manifest.Id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error stopping managed plugin {PluginId}", Manifest.Id);
        }
    }

    public override void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }
}

/// <summary>
/// Plugin context implementation — bridges IPlugin calls to device services.
/// </summary>
internal class PluginContext : IPluginContext
{
    private readonly ILogger _logger;
    private readonly IDeviceService _deviceService;

    public string PluginId { get; }

    public PluginContext(string pluginId, ILogger logger, IDeviceService deviceService)
    {
        PluginId = pluginId;
        _logger = logger;
        _deviceService = deviceService;
    }

    public Task SetButtonImageAsync(int buttonIndex, byte[] imageData, CancellationToken cancellationToken = default)
        => _deviceService.SendButtonImageAsync(0, (byte)buttonIndex, imageData, null, cancellationToken);

    public Task SetButtonTitleAsync(int buttonIndex, string title, CancellationToken cancellationToken = default)
        => _deviceService.SetButtonNameAsync(0, (byte)buttonIndex, title, cancellationToken);

    public Task SetLedColorAsync(int buttonIndex, byte r, byte g, byte b, CancellationToken cancellationToken = default)
        => _deviceService.SetLedColorAsync(0, (byte)buttonIndex, new LedConfig { R = r, G = g, B = b, Brightness = 100 }, cancellationToken);

    public async Task ShowAlertAsync(int buttonIndex, CancellationToken cancellationToken = default)
    {
        // Brief red flash to indicate alert
        await _deviceService.SetLedColorAsync(0, (byte)buttonIndex,
            new LedConfig { R = 255, G = 0, B = 0, Brightness = 100 }, cancellationToken);
        await Task.Delay(200, cancellationToken);
        await _deviceService.SetLedColorAsync(0, (byte)buttonIndex,
            new LedConfig { R = 0, G = 0, B = 0, Brightness = 0 }, cancellationToken);
    }

    public void LogInfo(string message) => _logger.LogInformation("[{PluginId}] {Message}", PluginId, message);
    public void LogWarning(string message) => _logger.LogWarning("[{PluginId}] {Message}", PluginId, message);
    public void LogError(string message, Exception? exception = null) =>
        _logger.LogError(exception, "[{PluginId}] {Message}", PluginId, message);

    public async Task<T?> GetSettingsAsync<T>(CancellationToken cancellationToken = default) where T : class
    {
        var path = GetSettingsPath();
        if (!File.Exists(path))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            return JsonConvert.DeserializeObject<T>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{PluginId}] Failed to load settings", PluginId);
            return null;
        }
    }

    public async Task SaveSettingsAsync<T>(T settings, CancellationToken cancellationToken = default) where T : class
    {
        var path = GetSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    private string GetSettingsPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MacroKeyboard", "plugins", PluginId, "settings.json");
}
