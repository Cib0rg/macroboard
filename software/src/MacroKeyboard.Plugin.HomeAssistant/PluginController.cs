using System.Text.Json;

namespace MacroKeyboard.Plugin.HomeAssistant;

public sealed class PluginController : IAsyncDisposable
{
    private readonly SdConnection _sd;
    private HaConnection? _ha;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();

    // context → button config
    private readonly Dictionary<string, ButtonConfig> _buttons = new();
    // entityId → last known HA state (includes friendly name)
    private readonly Dictionary<string, HaState> _entityStates = new(StringComparer.OrdinalIgnoreCase);
    // context of the currently open Property Inspector (null when PI is closed)
    private string? _currentPiContext;

    // reconnect state
    private string? _lastHaUrl;
    private string? _lastHaToken;
    private volatile bool _reconnecting;

    // periodic full-sync loop (fallback for missed state_changed events)
    private CancellationTokenSource? _syncCts;
    private const int SyncIntervalSeconds = 30;

    public PluginController(SdConnection sd)
    {
        _sd = sd;
        _sd.WillAppear                  += OnWillAppear;
        _sd.WillDisappear               += OnWillDisappear;
        _sd.KeyDown                     += OnKeyDown;
        _sd.DidReceiveSettings          += OnDidReceiveSettings;
        _sd.DidReceiveGlobalSettings    += OnDidReceiveGlobalSettings;
        _sd.SendToPlugin                += OnSendToPlugin;
        _sd.PropertyInspectorDidAppear  += OnPropertyInspectorDidAppear;
        _sd.PropertyInspectorDidDisappear += OnPropertyInspectorDidDisappear;
    }

    // ── SD event handlers ─────────────────────────────────────────────────────

    private async void OnWillAppear(JsonElement msg)
    {
        try
        {
            var context  = GetString(msg, "context");
            var settings = ParseButtonSettings(GetPayloadSettings(msg));
            _buttons[context] = new ButtonConfig(context, settings);
            await RefreshButtonAsync(context, settings.EntityId);
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Ctrl] WillAppear error: {ex}"); }
    }

    private void OnWillDisappear(JsonElement msg)
    {
        var context = GetString(msg, "context");
        _buttons.Remove(context);
    }

    private async void OnKeyDown(JsonElement msg)
    {
        try
        {
            var context = GetString(msg, "context");

            // keyDown payload carries latest sidecar settings — more up-to-date than willAppear.
            var payloadSettings = ParseButtonSettings(GetPayloadSettings(msg));
            if (!string.IsNullOrEmpty(payloadSettings.EntityId))
            {
                var existing = _buttons.GetValueOrDefault(context) ?? new ButtonConfig(context, payloadSettings);
                _buttons[context] = existing with { Settings = payloadSettings };
            }

            var cfg = _buttons.GetValueOrDefault(context) ?? new ButtonConfig(context, payloadSettings);
            Console.WriteLine($"[Ctrl] keyDown ctx={context} entity='{cfg.Settings.EntityId}' haConnected={_ha?.IsConnected}");

            if (_ha == null || !_ha.IsConnected)
            {
                Console.Error.WriteLine("[Ctrl] keyDown: HA not connected — press Test in PI first");
                await _sd.ShowAlertAsync(context);
                return;
            }

            if (string.IsNullOrEmpty(cfg.Settings.EntityId))
            {
                Console.Error.WriteLine("[Ctrl] keyDown: no entity configured — open PI and select an entity");
                await _sd.ShowAlertAsync(context);
                return;
            }

            var domain  = string.IsNullOrEmpty(cfg.Settings.ServiceDomain) ? "homeassistant" : cfg.Settings.ServiceDomain;
            var service = string.IsNullOrEmpty(cfg.Settings.Service)       ? "toggle"         : cfg.Settings.Service;
            Console.WriteLine($"[Ctrl] Calling {domain}.{service} on {cfg.Settings.EntityId}");
            await _ha.CallServiceAsync(domain, service, cfg.Settings.EntityId);
            await _sd.ShowOkAsync(context);
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Ctrl] keyDown error: {ex}"); }
    }

    private async void OnDidReceiveSettings(JsonElement msg)
    {
        try
        {
            var context  = GetString(msg, "context");
            var settings = ParseButtonSettings(GetPayloadSettings(msg));
            _buttons[context] = new ButtonConfig(context, settings);
            await RefreshButtonAsync(context, settings.EntityId);
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Ctrl] DidReceiveSettings error: {ex}"); }
    }

    private async void OnDidReceiveGlobalSettings(JsonElement msg)
    {
        try
        {
            var payload  = GetPayload(msg);
            var settings = ParseGlobalSettings(payload);

            Console.WriteLine($"[Ctrl] DidReceiveGlobalSettings: haUrl={settings.HaUrl}, hasToken={!string.IsNullOrEmpty(settings.HaToken)}");

            if (string.IsNullOrEmpty(settings.HaUrl) || string.IsNullOrEmpty(settings.HaToken))
            {
                Console.WriteLine("[Ctrl] Global settings missing HA URL or token — not connecting");
                return;
            }

            await ConnectHaAsync(settings.HaUrl, settings.HaToken);
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Ctrl] DidReceiveGlobalSettings error: {ex}"); }
    }

    private async void OnSendToPlugin(JsonElement msg)
    {
        var context = GetString(msg, "context");
        var payload = GetPayload(msg);
        var action  = payload.TryGetProperty("action", out var a) ? a.GetString() : null;

        try
        {
            switch (action)
            {
                case "checkConnection":
                {
                    var status  = (_ha?.IsConnected == true) ? "connected" : "disconnected";
                    var message = (_ha?.IsConnected == true) ? "" : "Not connected. Enter URL + token and click Test.";
                    await _sd.SendToPropertyInspectorAsync(context, new { action = "connectionStatus", status, message });
                    break;
                }
                case "testConnection":
                {
                    var haUrl   = GetStr(payload, "haUrl");
                    var haToken = GetStr(payload, "haToken");
                    await TestConnectionAsync(context, haUrl, haToken);
                    break;
                }
                case "getEntities":
                {
                    await SendEntitiesToPiAsync(context);
                    break;
                }
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Ctrl] OnSendToPlugin error: {ex}"); }
    }

    private async void OnPropertyInspectorDidAppear(JsonElement msg)
    {
        var context = GetString(msg, "context");
        _currentPiContext = context;
        Console.WriteLine($"[Ctrl] PI appeared for {context}");

        try
        {
            if (_ha?.IsConnected == true)
                await SendEntitiesToPiAsync(context);
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Ctrl] OnPropertyInspectorDidAppear error: {ex}"); }
    }

    private void OnPropertyInspectorDidDisappear(JsonElement msg)
    {
        _currentPiContext = null;
    }

    // ── HA connection management ──────────────────────────────────────────────

    private async Task ConnectHaAsync(string url, string token)
    {
        _lastHaUrl   = url;
        _lastHaToken = token;

        await _connectLock.WaitAsync(_disposeCts.Token);
        try
        {
            if (_ha != null)
            {
                _ha.StateChanged  -= OnHaStateChanged;
                _ha.Connected     -= OnHaConnected;
                _ha.Disconnected  -= OnHaDisconnected;
                await _ha.DisposeAsync();
                _ha = null;
            }

            var ha = new HaConnection();
            ha.StateChanged  += OnHaStateChanged;
            ha.Connected     += OnHaConnected;
            ha.Disconnected  += OnHaDisconnected;

            // Assign _ha BEFORE ConnectAsync — Connected fires synchronously at the end of
            // ConnectAsync (before it returns), so OnHaConnected must be able to reference _ha.
            _ha = ha;

            try
            {
                Console.WriteLine($"[Ctrl] Connecting to {url}…");
                await ha.ConnectAsync(url, token, _disposeCts.Token);
                Console.WriteLine($"[Ctrl] Connected to HA at {url}");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                ha.StateChanged  -= OnHaStateChanged;
                ha.Connected     -= OnHaConnected;
                ha.Disconnected  -= OnHaDisconnected;
                await ha.DisposeAsync();
                _ha = null;
                Console.Error.WriteLine($"[Ctrl] HA connect failed: {ex.Message}");
            }
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private async void OnHaDisconnected()
    {
        _syncCts?.Cancel();
        Console.Error.WriteLine("[Ctrl] HA disconnected — showing error state on all buttons");

        foreach (var (context, _) in _buttons.ToList())
        {
            try { await _sd.SetButtonErrorAsync(context, "HA\nOffline"); }
            catch (Exception ex) { Console.Error.WriteLine($"[Ctrl] SetButtonError failed: {ex.Message}"); }
        }

        if (_lastHaUrl != null && _lastHaToken != null && !_reconnecting && !_disposeCts.IsCancellationRequested)
        {
            _reconnecting = true;
            _ = Task.Run(async () =>
            {
                try   { await ReconnectHaLoopAsync(); }
                finally { _reconnecting = false; }
            });
        }
    }

    private async Task ReconnectHaLoopAsync()
    {
        var delay = TimeSpan.FromSeconds(5);
        Console.WriteLine("[Ctrl] Starting HA reconnect loop...");

        while (!_disposeCts.IsCancellationRequested)
        {
            try { await Task.Delay(delay, _disposeCts.Token); }
            catch (OperationCanceledException) { return; }

            if (_ha?.IsConnected == true) return; // connected by user action

            delay = TimeSpan.FromSeconds(Math.Min(60, delay.TotalSeconds * 2));
            Console.WriteLine($"[Ctrl] Attempting HA reconnect to {_lastHaUrl}...");

            try
            {
                await ConnectHaAsync(_lastHaUrl!, _lastHaToken!);
                if (_ha?.IsConnected == true)
                {
                    Console.WriteLine("[Ctrl] HA reconnected successfully");
                    return;
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { Console.Error.WriteLine($"[Ctrl] Reconnect attempt failed: {ex.Message}"); }
        }
    }

    private async Task TestConnectionAsync(string context, string haUrl, string haToken)
    {
        if (string.IsNullOrEmpty(haUrl) || string.IsNullOrEmpty(haToken))
        {
            await _sd.SendToPropertyInspectorAsync(context, new
            {
                action  = "connectionStatus",
                status  = "error",
                message = "Enter URL and token first"
            });
            return;
        }

        // Connect for real — not a throw-away test. If it succeeds, _ha is ready to handle
        // button presses immediately without waiting for the user to click "Save connection".
        await ConnectHaAsync(haUrl, haToken);

        if (_ha?.IsConnected == true)
        {
            await _sd.SendToPropertyInspectorAsync(context, new
            {
                action  = "connectionStatus",
                status  = "connected",
                message = ""
            });
            // Push entity list while PI is still open
            await SendEntitiesToPiAsync(context);
        }
        else
        {
            await _sd.SendToPropertyInspectorAsync(context, new
            {
                action  = "connectionStatus",
                status  = "error",
                message = "Connection failed — see backend log for details"
            });
        }
    }

    private async void OnHaConnected()
    {
        try
        {
            var states = await _ha!.GetAllStatesAsync();
            foreach (var (entityId, haState) in states)
                _entityStates[entityId] = haState;

            // Refresh all visible buttons
            foreach (var (context, cfg) in _buttons.ToList())
                await RefreshButtonAsync(context, cfg.Settings.EntityId, skipFetch: true);

            // Push entity list to PI if it's open
            if (_currentPiContext != null)
                await SendEntitiesToPiAsync(_currentPiContext);

            // Start periodic full-sync loop (one at a time)
            _syncCts?.Cancel();
            _syncCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
            _ = Task.Run(() => SyncLoopAsync(_syncCts.Token));
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Ctrl] OnHaConnected error: {ex}"); }
    }

    private async Task SyncLoopAsync(CancellationToken ct)
    {
        Console.WriteLine($"[Ctrl] Sync loop started (interval={SyncIntervalSeconds}s)");
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(SyncIntervalSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (_ha?.IsConnected != true) break;
                try { await PollAndRefreshAsync(ct); }
                catch (Exception ex) { Console.Error.WriteLine($"[Ctrl] Sync tick error: {ex.Message}"); }
            }
        }
        catch (OperationCanceledException) { }
        Console.WriteLine("[Ctrl] Sync loop exited");
    }

    private async Task PollAndRefreshAsync(CancellationToken ct)
    {
        var states = await _ha!.GetAllStatesAsync(ct);
        var updated = 0;
        foreach (var (context, cfg) in _buttons.ToList())
        {
            if (string.IsNullOrEmpty(cfg.Settings.EntityId)) continue;
            if (!states.TryGetValue(cfg.Settings.EntityId, out var newState)) continue;
            var prev = _entityStates.GetValueOrDefault(cfg.Settings.EntityId);
            if (prev?.State == newState.State) continue;
            _entityStates[newState.EntityId] = newState;
            Console.WriteLine($"[Ctrl] Sync detected change: {newState.EntityId} '{prev?.State}' → '{newState.State}'");
            await UpdateButtonDisplayAsync(context, cfg.Settings, newState.State);
            updated++;
        }
        if (updated > 0)
            Console.WriteLine($"[Ctrl] Sync complete: {updated} button(s) updated");
    }

    private async void OnHaStateChanged(HaState haState)
    {
        _entityStates[haState.EntityId] = haState;

        foreach (var (context, cfg) in _buttons.ToList())
        {
            if (!string.Equals(cfg.Settings.EntityId, haState.EntityId, StringComparison.OrdinalIgnoreCase))
                continue;
            Console.WriteLine($"[Ctrl] state_changed: {haState.EntityId} → '{haState.State}', updating button {context}");
            try { await UpdateButtonDisplayAsync(context, cfg.Settings, haState.State); }
            catch (Exception ex) { Console.Error.WriteLine($"[Ctrl]   → UpdateButtonDisplay failed: {ex}"); }
        }
    }

    // ── Entity list for PI ────────────────────────────────────────────────────

    private async Task SendEntitiesToPiAsync(string context)
    {
        if (_ha?.IsConnected != true)
        {
            await _sd.SendToPropertyInspectorAsync(context, new
            {
                action   = "entities",
                entities = (object?)null,
                reason   = "not connected"
            });
            return;
        }

        var entities = _entityStates.Values
            .Select(s =>
            {
                var dot    = s.EntityId.IndexOf('.');
                var domain = dot > 0 ? s.EntityId[..dot] : s.EntityId;
                var name   = string.IsNullOrEmpty(s.FriendlyName) ? s.EntityId : s.FriendlyName;
                return new { id = s.EntityId, name, domain, state = s.State };
            })
            .OrderBy(e => e.domain)
            .ThenBy(e => e.name)
            .ToArray();

        Console.WriteLine($"[Ctrl] Sending {entities.Length} entities to PI");
        await _sd.SendToPropertyInspectorAsync(context, new { action = "entities", entities });
    }

    // ── Display helpers ───────────────────────────────────────────────────────

    private async Task RefreshButtonAsync(string context, string entityId, bool skipFetch = false)
    {
        if (string.IsNullOrEmpty(entityId))
        {
            await _sd.SetButtonDisplayAsync(context, "HA", false);
            return;
        }

        // Skip full GET_STATES if cache is already warm (OnHaConnected populated it).
        // willAppear for individual buttons doesn't need to re-fetch all 1000+ entities.
        bool cacheWarm = _entityStates.Count > 0;
        if (!skipFetch && !cacheWarm && _ha?.IsConnected == true)
        {
            try
            {
                var states = await _ha.GetAllStatesAsync();
                foreach (var (id, s) in states)
                    _entityStates[id] = s;
            }
            catch { }
        }

        if (_entityStates.TryGetValue(entityId, out var haState) && _buttons.TryGetValue(context, out var cfg))
            await UpdateButtonDisplayAsync(context, cfg.Settings, haState.State);
    }

    private async Task UpdateButtonDisplayAsync(string context, ButtonSettings settings, string state)
    {
        var label = ResolveLabel(settings, _entityStates.GetValueOrDefault(settings.EntityId));
        var isOn  = IsActiveState(state);
        var displayText = $"{label}\n{(isOn ? "ON" : "OFF")}";
        await _sd.SetButtonDisplayAsync(context, displayText, isOn);
    }

    // ── Parsing helpers ───────────────────────────────────────────────────────

    private static ButtonSettings ParseButtonSettings(JsonElement settings)
    {
        if (settings.ValueKind != JsonValueKind.Object) return new ButtonSettings();
        return new ButtonSettings(
            EntityId      : GetStr(settings, "entityId"),
            Label         : GetStr(settings, "label"),
            ServiceDomain : GetStr(settings, "serviceDomain"),
            Service       : GetStr(settings, "service"));
    }

    private static GlobalSettings ParseGlobalSettings(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object) return new GlobalSettings();
        var settings = payload.TryGetProperty("settings", out var s) ? s : payload;
        return new GlobalSettings(
            HaUrl   : GetStr(settings, "haUrl"),
            HaToken : GetStr(settings, "haToken"));
    }

    private static JsonElement GetPayload(JsonElement msg)
        => msg.TryGetProperty("payload", out var p) ? p : default;

    private static JsonElement GetPayloadSettings(JsonElement msg)
    {
        var payload = GetPayload(msg);
        return payload.TryGetProperty("settings", out var s) ? s : default;
    }

    private static string GetString(JsonElement el, string key)
        => el.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";

    private static string GetStr(JsonElement el, string key)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(key, out var v)
            ? v.GetString() ?? "" : "";

    private static string ResolveLabel(ButtonSettings s, HaState? haState)
    {
        if (!string.IsNullOrEmpty(s.Label)) return s.Label;
        if (haState != null && !string.IsNullOrEmpty(haState.FriendlyName)) return haState.FriendlyName;
        if (!string.IsNullOrEmpty(s.EntityId))
        {
            var part = s.EntityId.Contains('.') ? s.EntityId[(s.EntityId.LastIndexOf('.') + 1)..] : s.EntityId;
            return Capitalize(part.Replace('_', ' '));
        }
        return "HA";
    }

    private static string Capitalize(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];

    private static bool IsActiveState(string state)
        => state.Equals("on",      StringComparison.OrdinalIgnoreCase)
        || state.Equals("open",    StringComparison.OrdinalIgnoreCase)
        || state.Equals("playing", StringComparison.OrdinalIgnoreCase)
        || state.Equals("active",  StringComparison.OrdinalIgnoreCase)
        || state.Equals("home",    StringComparison.OrdinalIgnoreCase)
        || state.Equals("locked",  StringComparison.OrdinalIgnoreCase);

    private static string ToDataUrl(byte[] png)
        => "data:image/png;base64," + Convert.ToBase64String(png);

    // ── IAsyncDisposable ──────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        _syncCts?.Cancel();
        _syncCts?.Dispose();
        _disposeCts.Cancel();
        if (_ha != null)
        {
            _ha.StateChanged  -= OnHaStateChanged;
            _ha.Connected     -= OnHaConnected;
            _ha.Disconnected  -= OnHaDisconnected;
            await _ha.DisposeAsync();
        }
        _connectLock.Dispose();
        _disposeCts.Dispose();
    }

    // ── Records ───────────────────────────────────────────────────────────────

    private sealed record ButtonConfig(string Context, ButtonSettings Settings);

    private sealed record ButtonSettings(
        string EntityId      = "",
        string Label         = "",
        string ServiceDomain = "",
        string Service       = "");

    private sealed record GlobalSettings(string HaUrl = "", string HaToken = "");
}
