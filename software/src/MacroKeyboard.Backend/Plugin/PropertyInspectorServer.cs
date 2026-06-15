using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;

namespace MacroKeyboard.Backend.Plugin;

/// <summary>
/// Lightweight HTTP file server that serves Property Inspector HTML/JS/CSS assets
/// from each plugin's directory.
///
/// URL scheme: http://localhost:8787/plugins/{pluginId}/{relativePath}
/// </summary>
public class PropertyInspectorServer : IDisposable
{
    public const int HttpPort = 8787;

    private readonly ILogger<PropertyInspectorServer> _logger;

    // pluginId → absolute directory path
    private readonly ConcurrentDictionary<string, string> _pluginDirs = new(StringComparer.OrdinalIgnoreCase);

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private bool _isRunning;

    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"]  = "text/html; charset=utf-8",
        [".htm"]   = "text/html; charset=utf-8",
        [".js"]    = "application/javascript; charset=utf-8",
        [".mjs"]   = "application/javascript; charset=utf-8",
        [".css"]   = "text/css; charset=utf-8",
        [".json"]  = "application/json; charset=utf-8",
        [".png"]   = "image/png",
        [".jpg"]   = "image/jpeg",
        [".jpeg"]  = "image/jpeg",
        [".svg"]   = "image/svg+xml",
        [".ico"]   = "image/x-icon",
        [".woff"]  = "font/woff",
        [".woff2"] = "font/woff2",
    };

    public PropertyInspectorServer(ILogger<PropertyInspectorServer> logger)
    {
        _logger = logger;
    }

    /// <summary>Maps pluginId → the directory that contains its PI assets.</summary>
    public void RegisterPlugin(string pluginId, string directory)
    {
        _pluginDirs[pluginId] = directory;
        _logger.LogInformation("PropertyInspectorServer: registered plugin {PluginId} → {Dir}", pluginId, directory);
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_isRunning)
        {
            _logger.LogWarning("PropertyInspectorServer is already running");
            return;
        }

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{HttpPort}/");
        _listener.Prefixes.Add($"http://127.0.0.1:{HttpPort}/");
        _listener.Start();

        _isRunning = true;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token), CancellationToken.None);

        _logger.LogInformation("PropertyInspectorServer started on port {Port}", HttpPort);
        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!_isRunning) return;
        _isRunning = false;
        _cts?.Cancel();
        _listener?.Stop();
        if (_acceptTask != null)
        {
            try { await _acceptTask.ConfigureAwait(false); }
            catch { /* expected cancellation */ }
        }
        _logger.LogInformation("PropertyInspectorServer stopped");
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _cts?.Dispose();
        _listener?.Close();
    }

    // ── Accept loop ───────────────────────────────────────────────────────────

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _isRunning)
        {
            try
            {
                var ctx = await _listener!.GetContextAsync().ConfigureAwait(false);
                // Fire-and-forget each request so we can keep accepting
                _ = Task.Run(() => HandleRequestAsync(ctx), CancellationToken.None);
            }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) when (!_isRunning) { break; }
            catch (Exception ex) when (_isRunning)
            {
                _logger.LogError(ex, "PropertyInspectorServer: error accepting request");
            }
        }
    }

    // ── Request handler ───────────────────────────────────────────────────────

    private async Task HandleRequestAsync(HttpListenerContext ctx)
    {
        var req  = ctx.Request;
        var resp = ctx.Response;

        // Always add CORS header
        resp.Headers["Access-Control-Allow-Origin"] = "*";

        // Handle pre-flight
        if (req.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            resp.Headers["Access-Control-Allow-Methods"] = "GET, OPTIONS";
            resp.Headers["Access-Control-Allow-Headers"] = "*";
            resp.StatusCode = 204;
            resp.Close();
            return;
        }

        // Only GET is meaningful for a file server
        if (!req.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            resp.StatusCode = 405;
            resp.Close();
            return;
        }

        try
        {
            // Expected path: /plugins/{pluginId}/{relativePath}
            var rawPath = req.Url?.AbsolutePath ?? "/";

            // Decode percent-encoding
            var decoded = Uri.UnescapeDataString(rawPath);

            // Normalise separators
            decoded = decoded.Replace('/', Path.DirectorySeparatorChar);

            // Split: [0]="" [1]="plugins" [2]=pluginId [3..]=relative parts
            var segments = decoded.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length < 3
                || !string.Equals(segments[0], "plugins", StringComparison.OrdinalIgnoreCase))
            {
                Send404(resp, rawPath);
                return;
            }

            var pluginId     = segments[1];
            var relativeParts = segments[2..]; // everything after pluginId

            if (!_pluginDirs.TryGetValue(pluginId, out var baseDir))
            {
                _logger.LogWarning("PropertyInspectorServer: unknown pluginId '{PluginId}'", pluginId);
                Send404(resp, rawPath);
                return;
            }

            // Build the candidate path and check for path-traversal
            var candidatePath = Path.GetFullPath(Path.Combine(baseDir, Path.Combine(relativeParts)));
            var canonicalBase = Path.GetFullPath(baseDir);

            if (!candidatePath.StartsWith(canonicalBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !candidatePath.Equals(canonicalBase, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "PropertyInspectorServer: path traversal attempt blocked: {Path}", candidatePath);
                resp.StatusCode = 403;
                resp.Close();
                return;
            }

            if (!File.Exists(candidatePath))
            {
                Send404(resp, rawPath);
                return;
            }

            var ext      = Path.GetExtension(candidatePath);
            var mimeType = MimeTypes.TryGetValue(ext, out var mt) ? mt : "application/octet-stream";

            var bytes = await File.ReadAllBytesAsync(candidatePath).ConfigureAwait(false);

            // For HTML pages loaded with SD connection params, inject the autoconnect script.
            // This mirrors how Stream Deck software initialises PIs: it calls
            // connectElgatoStreamDeckSocket() externally rather than having each page read
            // window.location.search itself.
            if ((ext == ".html" || ext == ".htm") && req.QueryString["registerEvent"] != null)
                bytes = InjectAutoConnectScript(bytes);

            resp.StatusCode    = 200;
            resp.ContentType   = mimeType;
            resp.ContentLength64 = bytes.Length;

            await resp.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            resp.OutputStream.Close();

            _logger.LogDebug("PropertyInspectorServer: 200 {Path}", rawPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PropertyInspectorServer: error handling {Url}", req.Url);
            try
            {
                resp.StatusCode = 500;
                resp.Close();
            }
            catch { /* response may already be partially written */ }
        }
    }

    private static byte[] InjectAutoConnectScript(byte[] htmlBytes)
    {
        const string script = """
<script>
(function() {
  var p = new URLSearchParams(window.location.search);
  var port       = p.get('port');
  var uuid       = p.get('propertyInspectorUUID');
  var evt        = p.get('registerEvent');
  var info       = p.get('info');
  var actionInfo = p.get('actionInfo');
  if (!port || !uuid || !evt) return;
  function tryConnect() {
    if (typeof connectElgatoStreamDeckSocket === 'function') {
      connectElgatoStreamDeckSocket(port, uuid, evt, info, actionInfo);
    } else {
      setTimeout(tryConnect, 50);
    }
  }
  tryConnect();
})();
</script>
""";
        var html = System.Text.Encoding.UTF8.GetString(htmlBytes);
        var idx  = html.IndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        html = idx >= 0
            ? html.Insert(idx, script)
            : html + script;
        return System.Text.Encoding.UTF8.GetBytes(html);
    }

    private static void Send404(HttpListenerResponse resp, string path)
    {
        resp.StatusCode = 404;
        resp.Close();
    }
}
