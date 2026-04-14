using MacroKeyboard.Shared.Plugin;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Concurrent;

namespace MacroKeyboard.Backend.Plugin;

/// <summary>
/// Manages plugin lifecycle and registration
/// </summary>
public class PluginManager : IDisposable
{
    private readonly ILogger<PluginManager> _logger;
    private readonly string _pluginsDirectory;
    private readonly ConcurrentDictionary<string, PluginInstance> _plugins = new();
    private readonly ConcurrentDictionary<string, PluginManifest> _manifests = new();

    public PluginManager(ILogger<PluginManager> logger, string pluginsDirectory)
    {
        _logger = logger;
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
            "managed" => new ManagedPluginInstance(manifest, pluginDir, _logger),
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
    public ManagedPluginInstance(PluginManifest manifest, string pluginDirectory, ILogger logger)
        : base(manifest, pluginDirectory, logger)
    {
    }

    public override Task StartAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Implement managed plugin loading using Assembly.LoadFrom
        Logger.LogWarning("Managed plugins are not yet implemented");
        return Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public override void Dispose()
    {
    }
}
