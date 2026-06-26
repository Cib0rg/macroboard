using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacroKeyboard.Shared.Plugin;
using Newtonsoft.Json;
using System;
using System.Collections.ObjectModel;

namespace MacroKeyboard.UI.ViewModels;

public partial class ButtonConfigDialogViewModel
{
    // ── Plugin fields ─────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _pluginId = string.Empty;

    [ObservableProperty]
    private string _pluginActionId = string.Empty;

    [ObservableProperty]
    private string _pluginSettings = string.Empty;

    [ObservableProperty]
    private string _pluginSearchText = string.Empty;

    [ObservableProperty]
    private PluginActionInfo? _selectedPluginAction;

    [ObservableProperty]
    private string? _propertyInspectorUrl;

    public ObservableCollection<PluginActionInfo> AvailablePluginActions { get; } = new();
    public ObservableCollection<PluginActionInfo> FilteredPluginActions  { get; } = new();

    // ── Computed properties ───────────────────────────────────────────────────

    public bool HasPropertyInspector => !string.IsNullOrEmpty(PropertyInspectorUrl);

    public string? PropertyInspectorSourceUrl
    {
        get
        {
            if (string.IsNullOrEmpty(PropertyInspectorUrl)) return null;
            var ctx        = $"{PluginId}:{ButtonConfig.ButtonId}";
            var info       = BuildPiInfoJson();
            var actionInfo = BuildPiActionInfoJson(ctx);
            var query = string.Concat(
                "port=28196",
                "&propertyInspectorUUID=", Uri.EscapeDataString(ctx),
                "&registerEvent=registerPropertyInspector",
                "&info=", Uri.EscapeDataString(info),
                "&actionInfo=", Uri.EscapeDataString(actionInfo));
            return new UriBuilder(PropertyInspectorUrl) { Query = query }.Uri.ToString();
        }
    }

    // ── Change handlers ───────────────────────────────────────────────────────

    partial void OnPropertyInspectorUrlChanged(string? value)
    {
        OnPropertyChanged(nameof(HasPropertyInspector));
        OnPropertyChanged(nameof(PropertyInspectorSourceUrl));
    }

    partial void OnPluginIdChanged(string value)
        => OnPropertyChanged(nameof(PropertyInspectorSourceUrl));

    partial void OnSelectedPluginActionChanged(PluginActionInfo? value)
    {
        if (value == null)
        {
            PropertyInspectorUrl = null;
            return;
        }
        // PluginId must be set BEFORE PropertyInspectorUrl so PropertyInspectorSourceUrl
        // uses the correct context when the binding refreshes.
        PluginId             = value.PluginId;
        PluginActionId       = value.ActionId;
        PropertyInspectorUrl = value.PropertyInspectorUrl;
        if (string.IsNullOrEmpty(ImagePath) && !string.IsNullOrEmpty(value.IconPath))
            ImagePath = value.IconPath;
        OnPropertyChanged(nameof(CurrentActionDisplayName));
    }

    partial void OnPluginSearchTextChanged(string value) => ApplyPluginFilter();

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void ClearPluginAction()
    {
        SelectedPluginAction = null;
        PluginId             = string.Empty;
        PluginActionId       = string.Empty;
        PropertyInspectorUrl = null;
        PluginSearchText     = string.Empty;
        ApplyPluginFilter();
        OnPropertyChanged(nameof(CurrentActionDisplayName));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ApplyPluginFilter()
    {
        FilteredPluginActions.Clear();
        var q = PluginSearchText?.Trim() ?? string.Empty;
        foreach (var pa in AvailablePluginActions)
        {
            if (q.Length == 0
                || pa.ActionName.Contains(q, StringComparison.OrdinalIgnoreCase)
                || pa.PluginName.Contains(q, StringComparison.OrdinalIgnoreCase))
                FilteredPluginActions.Add(pa);
        }
    }

    public string GetPropertyInspectorConnectScript()
    {
        var ctx        = $"{PluginId}:{ButtonConfig.ButtonId}";
        var info       = BuildPiInfoJson();
        var actionInfo = BuildPiActionInfoJson(ctx);
        static string Esc(string s) => s.Replace("\\", "\\\\").Replace("'", "\\'");
        return $"if(typeof connectElgatoStreamDeckSocket==='function'){{connectElgatoStreamDeckSocket('28196','{ctx}','registerPropertyInspector','{Esc(info)}','{Esc(actionInfo)}')}}";
    }

    private string BuildPiInfoJson() => JsonConvert.SerializeObject(new
    {
        application      = new { font = "Arial", language = "en", platform = "windows", platformVersion = "10.0.0", version = "1.0.0" },
        plugin           = new { uuid = PluginId, version = "1.0.0" },
        devicePixelRatio = 1,
        colors           = new { },
        devices          = new[] { new { id = "MK_DEVICE_0", name = "MacroKeyboard", size = new { columns = 5, rows = 2 }, type = 0 } }
    });

    private string BuildPiActionInfoJson(string context)
    {
        object? settingsObj = null;
        if (!string.IsNullOrEmpty(PluginSettings))
            try { settingsObj = JsonConvert.DeserializeObject(PluginSettings); } catch { }
        return JsonConvert.SerializeObject(new
        {
            action  = PluginActionId,
            context = context,
            device  = "MK_DEVICE_0",
            payload = new
            {
                settings    = settingsObj ?? (object)new { },
                coordinates = new { column = ButtonConfig.ButtonId % 5, row = ButtonConfig.ButtonId / 5 }
            }
        });
    }
}
