using MacroKeyboard.Shared.Plugin;
using Newtonsoft.Json;
using System.IO.Compression;

namespace MacroKeyboard.Backend.Plugin;

public partial class PluginManager
{
    // ── Plugin discovery ──────────────────────────────────────────────────────

    public async Task LoadPluginsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Loading plugins from {Dir}", _pluginsDirectory);

        if (!Directory.Exists(_pluginsDirectory))
        {
            _logger.LogWarning("Plugins directory does not exist: {Dir}", _pluginsDirectory);
            Directory.CreateDirectory(_pluginsDirectory);
            return;
        }

        // Extract plugin archives (.streamDeckPlugin or .zip) that haven't been unpacked yet,
        // or that are newer than their previously extracted directory (dev rebuild scenario).
        var loadedFromArchive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var archivePath in Directory.GetFiles(_pluginsDirectory)
                     .Where(f => f.EndsWith(".streamDeckPlugin", StringComparison.OrdinalIgnoreCase)
                               || f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var pluginDir = await ExtractStreamDeckPluginAsync(archivePath, cancellationToken);
                if (pluginDir != null)
                {
                    loadedFromArchive.Add(pluginDir);
                    await LoadPluginAsync(pluginDir, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract/load plugin archive {File}", Path.GetFileName(archivePath));
            }
        }

        // Load already-extracted plugin directories (skip ones we just loaded from archives).
        foreach (var pluginDir in Directory.GetDirectories(_pluginsDirectory))
        {
            if (loadedFromArchive.Contains(pluginDir)) continue;
            try { await LoadPluginAsync(pluginDir, cancellationToken); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to load plugin from {Dir}", pluginDir); }
        }

        _logger.LogInformation("Loaded {Count} plugins", _plugins.Count);
    }

    /// <summary>
    /// Extracts a .streamDeckPlugin archive (ZIP) to the plugins directory.
    /// Returns the path to the extracted plugin directory, or null on failure.
    /// Re-uses an existing directory if the archive was already unpacked.
    /// </summary>
    private async Task<string?> ExtractStreamDeckPluginAsync(string archivePath, CancellationToken cancellationToken)
    {
        var archiveFileName = Path.GetFileName(archivePath);

        using var archive = ZipFile.OpenRead(archivePath);

        // Detect the single top-level folder that most .streamDeckPlugin archives wrap their files in.
        // E.g. "com.rgpaul.vlc.streamDeckPlugin" typically contains "com.rgpaul.vlc.sdPlugin/" at the root.
        var topLevelDirs = archive.Entries
            .Where(e => e.FullName.Contains('/'))
            .Select(e => e.FullName[..e.FullName.IndexOf('/')])
            .Where(d => d.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        bool hasSingleRoot = topLevelDirs.Count == 1;
        string pluginDirName;

        if (hasSingleRoot)
        {
            // Archive has one top-level folder — use it as the plugin directory name.
            pluginDirName = topLevelDirs[0];
        }
        else
        {
            // Files are at the archive root — derive the directory name from the archive name.
            string baseName;
            if (archiveFileName.EndsWith(".streamDeckPlugin", StringComparison.OrdinalIgnoreCase))
                baseName = archiveFileName[..^".streamDeckPlugin".Length];
            else
                baseName = Path.GetFileNameWithoutExtension(archiveFileName);
            pluginDirName = baseName + ".sdPlugin";
        }

        var targetDir = Path.Combine(_pluginsDirectory, pluginDirName);

        if (Directory.Exists(targetDir))
        {
            // Re-extract when the archive is newer than the directory (e.g. after a dev rebuild).
            var archiveTime = File.GetLastWriteTimeUtc(archivePath);
            var dirTime     = Directory.GetLastWriteTimeUtc(targetDir);
            if (archiveTime <= dirTime)
            {
                _logger.LogDebug("Plugin archive up to date, skipping extraction: {Archive}", archiveFileName);
                return targetDir;
            }
            _logger.LogInformation("Plugin archive is newer — re-extracting: {Archive}", archiveFileName);
            Directory.Delete(targetDir, recursive: true);
        }

        _logger.LogInformation("Extracting plugin archive: {Archive} → {Dir}", archiveFileName, pluginDirName);
        Directory.CreateDirectory(targetDir);
        var canonicalTarget = Path.GetFullPath(targetDir) + Path.DirectorySeparatorChar;
        int fileCount = 0;

        foreach (var entry in archive.Entries)
        {
            // Strip the single top-level prefix when present so files land directly in targetDir.
            string relPath = hasSingleRoot
                ? entry.FullName[(entry.FullName.IndexOf('/') + 1)..]
                : entry.FullName;

            if (relPath.Length == 0) continue;
            relPath = relPath.Replace('/', Path.DirectorySeparatorChar);

            var destPath = Path.GetFullPath(Path.Combine(targetDir, relPath));

            // Guard against path-traversal entries.
            if (!destPath.StartsWith(canonicalTarget, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Skipping unsafe archive entry: {Entry}", entry.FullName);
                continue;
            }

            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(destPath);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                await Task.Run(() => entry.ExtractToFile(destPath, overwrite: false), cancellationToken);
                fileCount++;
            }
        }

        _logger.LogInformation("Extracted {Dir}: {Count} files", pluginDirName, fileCount);
        return targetDir;
    }

    private async Task LoadPluginAsync(string pluginDir, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(pluginDir, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            _logger.LogWarning("No manifest.json in {Dir}", pluginDir);
            return;
        }

        var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
        var manifest = JsonConvert.DeserializeObject<PluginManifest>(manifestJson, _manifestSerializerSettings);

        if (manifest == null)
        {
            _logger.LogWarning("Failed to parse manifest: {Path}", manifestPath);
            return;
        }

        // Stream Deck manifests don't have an Id field — derive from folder name.
        // SD folders are named like "com.elgato.counter.sdPlugin"; strip the .sdPlugin suffix.
        if (string.IsNullOrEmpty(manifest.Id))
        {
            var folderName = Path.GetFileName(pluginDir);
            manifest.Id = folderName.EndsWith(".sdPlugin", StringComparison.OrdinalIgnoreCase)
                ? folderName[..^".sdPlugin".Length]
                : folderName;
        }

        // Stream Deck manifests: default type to executable
        if (manifest.IsStreamDeckFormat && string.IsNullOrEmpty(manifest.Type))
            manifest.Type = "executable";

        _logger.LogInformation("Loading plugin: {Name} v{Version} [{Type}]",
            manifest.Name, manifest.Version, manifest.Type);

        _manifests[manifest.Id]    = manifest;
        _pluginDirectories[manifest.Id] = pluginDir;
        _piServer.RegisterPlugin(manifest.Id, pluginDir);

        PluginInstance? instance = manifest.Type switch
        {
            "executable" => new ExecutablePluginInstance(manifest, pluginDir, _logger),
            "managed"    => new ManagedPluginInstance(manifest, pluginDir, _logger, _deviceService),
            _            => null
        };

        if (instance != null)
        {
            _plugins[manifest.Id] = instance;
            _logger.LogInformation("Plugin loaded: {Id}", manifest.Id);
        }
        else
        {
            _logger.LogWarning("Unsupported plugin type '{Type}' for {Id}", manifest.Type, manifest.Id);
        }
    }
}
