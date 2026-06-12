# Plugin System — Plan

## Текущее состояние

Скелет написан, но ни в чём не подключён:
- `PluginManager` — дискавери и загрузка плагинов (executable + managed), не инстанциируется в DI
- `WebSocketServer` — WS на порту 28196 (совместим с Stream Deck API), не стартует
- `IPlugin` / `IPluginContext` / `PluginManifest` — интерфейсы готовы
- `ActionExecutorService` — нет case для Plugin actions
- `PluginContext` — все методы заглушки (`// TODO`)
- `CustomHid` — неправильная архитектура (отсылает данные на PC вместо прямой отправки USB HID)

## Архитектурные решения

### Два разных механизма — не путать

**Plugin Action** (`ActionType.Plugin = 0x0B`) — кнопка вызывает конкретный action конкретного плагина. Полностью на PC-стороне: firmware просто сообщает "нажата кнопка X", backend читает профиль, находит `PluginId + ActionId + Settings`, маршрутизирует в плагин.

**Custom HID** (`ActionType.CustomHid = 0x02`) — firmware сам посылает raw USB HID report напрямую в систему. PC не участвует. Сейчас реализован наоборот — надо переделать в firmware.

### Плагины бывают двух типов

- **Executable** (Node.js, Python и т.д.) — запускаются как отдельный процесс, общаются через WebSocket на порту 28196 с Stream Deck-совместимым протоколом (`keyDown`, `keyUp`, `didReceiveSettings`, и т.д.)
- **Managed** (.NET DLL) — загружаются в изолированный `AssemblyLoadContext`, вызываются напрямую через `IPlugin`

### Как кнопка знает о плагине

Профиль хранит `PluginActionConfig { PluginId, ActionId, Settings }`. Firmware хранит это как непрозрачный blob (action_type=0x0B + raw bytes). При нажатии firmware шлёт EVENT_BUTTON_PRESSED с buttonId. Backend читает свою копию профиля, достаёт `PluginActionConfig`, маршрутизирует.

### UI: откуда берётся список plugin actions

Backend держит список загруженных манифестов. UI запрашивает его через IPC при открытии редактора. Actions из манифестов добавляются в палитру (правая колонка) после стандартных actions.

---

## Фазы

---

### Фаза 1 — Core Models

**Файлы:**
- `MacroKeyboard.Core/Models/ActionType.cs`
- `MacroKeyboard.Core/Models/ActionConfig.cs`
- `MacroKeyboard.Core/Plugin/IPlugin.cs`

**Задачи:**

1. Добавить `Plugin = 0x0B` в enum `ActionType`.

2. Добавить класс `PluginActionConfig` в `ActionConfig.cs`:
   ```csharp
   public class PluginActionConfig : ActionConfig
   {
       public override ActionType ActionType => ActionType.Plugin;
       public string PluginId { get; set; } = string.Empty;
       public string ActionId { get; set; } = string.Empty;
       public string? Settings { get; set; }  // JSON строка с настройками action

       public override byte[] ToBytes()
       {
           // [pluginId\0actionId\0settings\0] — null-terminated strings
           var pluginIdBytes = Encoding.UTF8.GetBytes(PluginId + "\0");
           var actionIdBytes = Encoding.UTF8.GetBytes(ActionId + "\0");
           var settingsBytes = Encoding.UTF8.GetBytes((Settings ?? "") + "\0");
           return [..pluginIdBytes, ..actionIdBytes, ..settingsBytes];
       }
   }
   ```
   Добавить в `FromActionType` factory и в JSON десериализацию (`IpcCommandHandler`).

3. Обновить `IPlugin.OnButtonPressedAsync` — передавать actionId и settings:
   ```csharp
   Task OnButtonPressedAsync(string actionId, string? settings, int buttonIndex, CancellationToken ct = default);
   Task OnButtonReleasedAsync(string actionId, string? settings, int buttonIndex, CancellationToken ct = default);
   ```

---

### Фаза 2 — Firmware

**Файлы:**
- `firmware/main/profile/profile_types.h`
- `firmware/main/profile/action_executor.c`
- `firmware/main/hardware/usb_hid_keyboard.h` / `.c`

**Задачи:**

1. Добавить `ACTION_TYPE_PLUGIN = 0x0B` в `action_type_t`.
   Добавить метку `"Plugin"` в `profile_manager.c` (label switch).

2. Добавить case `ACTION_TYPE_PLUGIN` в `execute_single_action`:
   ```c
   case ACTION_TYPE_PLUGIN:
       // Поведение идентично обычному нажатию — backend читает профиль сам
       // Firmware просто репортит кнопку, весь dispatch на PC
       // Ничего дополнительно делать не надо
       break;
   ```
   (EVENT_BUTTON_PRESSED уже отсылается общим кодом после switch.)

3. **Исправить Custom HID** — убрать отсылку на PC, вместо этого слать raw USB HID report:
   ```c
   case ACTION_TYPE_CUSTOM_HID:
       if (data_len > 0)
           usb_hid_send_raw_report(data, data_len);
       break;
   ```
   Добавить `usb_hid_send_raw_report(const uint8_t* data, uint16_t len)` в `usb_hid_keyboard.h` и реализовать в `.c` (передать в нужный HID endpoint).

---

### Фаза 3 — Backend: PluginContext + PluginManager

**Файлы:**
- `MacroKeyboard.Backend/Plugin/PluginManager.cs`

**Задачи:**

1. Внедрить `IDeviceService` в `PluginContext` (сейчас он принимает только `ILogger`):
   ```csharp
   internal class PluginContext : IPluginContext
   {
       private readonly IDeviceService _deviceService;
       // ...
   }
   ```

2. Реализовать все заглушки в `PluginContext`:
   - `SetButtonImageAsync` → `_deviceService.SendButtonImageAsync(0, buttonIndex, imageData)`
   - `SetButtonTitleAsync` → `_deviceService.SetButtonNameAsync(0, buttonIndex, title)`
   - `SetLedColorAsync` → `_deviceService.SetLedColorAsync(0, buttonIndex, new LedConfig { R=r, G=g, B=b })`
   - `ShowAlertAsync` → flash LED: выкл на 200ms, вкл обратно
   - `GetSettingsAsync` / `SaveSettingsAsync` → JSON файл в `%APPDATA%\MacroKeyboard\plugins\{pluginId}\settings.json`

3. Добавить `DispatchButtonPressAsync` в `PluginManager`:
   ```csharp
   public async Task DispatchButtonPressAsync(string pluginId, string actionId,
       string? settings, int buttonIndex, CancellationToken ct = default)
   ```
   - Для managed плагина: вызвать `_pluginInstance.OnButtonPressedAsync(actionId, settings, buttonIndex, ct)`
   - Для executable плагина: отправить через `WebSocketServer.BroadcastAsync` событие `keyDown` в формате Stream Deck:
     ```json
     { "event": "keyDown", "action": "{actionId}", "context": "{pluginId}:{buttonIndex}", "payload": { "settings": {...}, "coordinates": {...} } }
     ```

4. Добавить аналогичный `DispatchButtonReleaseAsync`.

5. Добавить `GetLoadedActions()`:
   ```csharp
   public IEnumerable<(string PluginId, string PluginName, PluginAction Action)> GetLoadedActions()
   ```
   Нужен для UI — формирует список доступных plugin actions для палитры.

---

### Фаза 4 — Backend: DI и жизненный цикл

**Файлы:**
- `MacroKeyboard.Backend/Program.cs`
- `MacroKeyboard.Backend/BackendService.cs`

**Задачи:**

1. Зарегистрировать в `Program.cs`:
   ```csharp
   builder.Services.AddSingleton<PluginManager>(sp => new PluginManager(
       sp.GetRequiredService<ILogger<PluginManager>>(),
       Path.Combine(AppContext.BaseDirectory, "plugins")));
   builder.Services.AddSingleton<WebSocketServer>();
   ```

2. В `BackendService.StartAsync`:
   ```csharp
   await _webSocketServer.StartAsync(ct);
   await _pluginManager.LoadPluginsAsync(ct);
   // start all loaded plugins
   foreach (var manifest in _pluginManager.GetPlugins())
       await _pluginManager.StartPluginAsync(manifest.Id, ct);
   ```

3. В `BackendService.StopAsync` — остановить плагины и WS сервер.

---

### Фаза 5 — Backend: ActionExecutorService + IPC

**Файлы:**
- `MacroKeyboard.Backend/Services/ActionExecutorService.cs`
- `MacroKeyboard.Backend/Services/IpcCommandHandler.cs`
- `MacroKeyboard.Shared/IPC/IpcMessage.cs`

**Задачи:**

1. Внедрить `PluginManager` в `ActionExecutorService`. Добавить case:
   ```csharp
   case ActionType.Plugin:
       await ExecutePluginActionAsync(e.ProfileId, e.ButtonId);
       break;
   ```
   ```csharp
   private async Task ExecutePluginActionAsync(byte profileId, byte buttonId)
   {
       var profile = await _profileService.GetProfileAsync(profileId);
       var button = profile?.Buttons.FirstOrDefault(b => b.ButtonId == buttonId);
       if (button?.Action is not PluginActionConfig action) return;
       await _pluginManager.DispatchButtonPressAsync(
           action.PluginId, action.ActionId, action.Settings, buttonId);
   }
   ```

2. Добавить IPC команду `plugin.list` в `IpcCommandHandler`:
   - Обрабатывает запрос от UI
   - Возвращает список `(pluginId, pluginName, actionId, actionName, actionIcon, tooltip)` из `_pluginManager.GetLoadedActions()`

3. Добавить `MessageType` константу `"plugin.list"` в `IpcMessage.cs`.

---

### Фаза 6 — UI: палитра + настройка plugin actions

**Файлы:**
- `MacroKeyboard.UI/ViewModels/ProfileEditorViewModel.cs`
- `MacroKeyboard.UI/ViewModels/ButtonConfigDialogViewModel.cs`
- `MacroKeyboard.UI/Views/ProfileEditorView.axaml`

**Задачи:**

1. В `ProfileEditorViewModel` при инициализации запросить плагины через IPC:
   ```csharp
   var pluginActions = await _ipcClient.SendRequestAsync<List<PluginActionInfo>>("plugin.list");
   foreach (var pa in pluginActions)
   {
       ActionPaletteItems.Add(new ActionPaletteItem(ActionType.Plugin, pa.ActionName, pa.Icon, pa.Tooltip)
       {
           PreConfiguredAction = new PluginActionConfig
           {
               PluginId = pa.PluginId,
               ActionId = pa.ActionId
           }
       });
   }
   ```
   Плагины добавляются в конец палитры после "None", сгруппированные по плагину.

2. В `ButtonConfigDialogViewModel`:
   - Добавить `IsPluginAction => SelectedActionType == ActionType.Plugin`
   - Добавить свойства `PluginId`, `PluginActionId`, `PluginSettings` (string, JSON)
   - В `Save()` добавить case:
     ```csharp
     ActionType.Plugin => new PluginActionConfig
     {
         PluginId = PluginId,
         ActionId = PluginActionId,
         Settings = string.IsNullOrWhiteSpace(PluginSettings) ? null : PluginSettings
     }
     ```
   - В `CurrentActionDisplayName` добавить `ActionType.Plugin => $"Plugin: {PluginActionId}"`
   - В `CurrentActionIcon` добавить `ActionType.Plugin => "🔌"`
   - В загрузке (`if (buttonConfig.Action is PluginActionConfig pa)`) — заполнить поля

3. В `ProfileEditorView.axaml` добавить секцию для Plugin action (появляется когда `IsPluginAction`):
   ```xml
   <Border IsVisible="{Binding IsPluginAction}" ...>
     <StackPanel>
       <TextBlock Text="PLUGIN ACTION" />
       <TextBlock Text="{Binding PluginId}" Opacity="0.6" />  <!-- read-only -->
       <TextBlock Text="Settings (JSON):" />
       <TextBox Text="{Binding PluginSettings}" AcceptsReturn="True" Height="80"
                PlaceholderText="{}" FontFamily="Monospace" />
     </StackPanel>
   </Border>
   ```

4. В `FlattenedButtonItem.Label` / `ActionText` добавить отображение для `PluginActionConfig`:
   ```csharp
   PluginActionConfig pa => $"🔌 {pa.ActionId}",
   ```

---

### Фаза 7 — Custom HID: UI fix

**Файлы:**
- `MacroKeyboard.UI/ViewModels/ButtonConfigDialogViewModel.cs`
- `MacroKeyboard.UI/Views/ProfileEditorView.axaml`
- `MacroKeyboard.Backend/Services/ActionExecutorService.cs`

После исправления firmware Custom HID больше не требует PC-обработки. Нужно:

1. В UI заменить "coming soon" на поле ввода hex-данных (уже есть в `SequenceStepViewModel` — скопировать паттерн):
   - Добавить `CustomHidData` property в `ButtonConfigDialogViewModel`
   - Добавить `ParseHexString` (скопировать из `SequenceStepViewModel`)
   - В `Save()` добавить: `ActionType.CustomHid => new CustomHidAction { Data = ParseHexString(CustomHidData) }`
   - В `ProfileEditorView.axaml` заменить "coming soon" на `<TextBox Text="{Binding CustomHidData}" Placeholder="FF 00 A0 ..." />`

2. Убрать Custom HID из `ActionExecutorService` (он не нужен — firmware всё делает сам). Ничего добавлять не надо, просто не добавлять case.

---

## Зависимости между фазами

```
Фаза 1 (Core Models)
    ├── Фаза 2 (Firmware) — независима, можно параллельно
    ├── Фаза 3 (PluginManager) — зависит от Фазы 1 (новый IPlugin.OnButtonPressedAsync)
    │       └── Фаза 4 (DI) — зависит от Фазы 3
    │               └── Фаза 5 (ActionExecutor + IPC) — зависит от Фаз 1, 3, 4
    │                       └── Фаза 6 (UI) — зависит от Фазы 5
    └── Фаза 7 (Custom HID fix) — почти независима, только Фаза 2 нужна для firmware части
```

## Что НЕ входит в план (за рамками v1)

- **Property Inspector** — загрузка HTML-файла плагина как настроечного UI (полная Stream Deck совместимость). В v1 достаточно JSON text area.
- **Hot reload плагинов** — сейчас требует перезапуска backend.
- **Plugin marketplace / установщик** — просто папка `plugins/` рядом с бинарником.
- **Версионирование API** — `MinimumBackendVersion` в манифесте пока не проверяется.
- **Права доступа плагинов** — плагин может делать всё что позволяет `IPluginContext`, без ограничений.
