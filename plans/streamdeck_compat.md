# Stream Deck Protocol Compatibility — Plan

> **Контекст:** Базовая система плагинов (`plugins.md`) уже реализована.
> Этот план добавляет совместимость с реальными плагинами Elgato Stream Deck —
> чтобы существующий плагин из Stream Deck Store можно было положить в папку
> `plugins/` и он заработал без изменений кода.

---

## Что такое "совместимость" конкретно

Stream Deck плагин — это папка с `manifest.json` и исполняемым файлом (обычно Node.js).
Приложение запускает его процесс с CLI-аргументами, плагин подключается к WebSocket,
проходит регистрацию, получает события и отвечает командами.

**Полная совместимость** = плагин работает без модификаций.
**Без Property Inspector** = логика работает, но UI конфигурации не отображается.

---

## Что уже есть (из plugins.md)

- `WebSocketServer` на порту 28196 — стартует
- `keyDown` / `keyUp` события отправляются плагину
- `PluginManager.DispatchButtonPressAsync/Release` — маршрутизация

---

## Что НЕ реализовано и нужно для совместимости

### Фаза 1 — Запуск и регистрация (Medium, ~2 дня)

Stream Deck запускает плагин строго так:
```
plugin.exe -port 28196 -pluginUUID <uuid> -registerEvent registerPlugin -info <deviceInfoJson>
```

Без этих аргументов любой реальный плагин упадёт при старте или зависнет в ожидании.

**Что нужно сделать:**

1. **`ExecutablePluginInstance.StartAsync`** — передавать аргументы:
   - `-port 28196`
   - `-pluginUUID <новый Guid на каждый запуск или стабильный хэш от PluginId>`
   - `-registerEvent registerPlugin`
   - `-info <json>` — JSON с info о приложении и устройстве (см. структуру ниже)

2. **`WebSocketServer`** — обработать входящее сообщение `registerPlugin`:
   ```json
   { "event": "registerPlugin", "uuid": "<pluginUUID>" }
   ```
   После получения: сопоставить UUID с `ExecutablePluginInstance`, пометить как зарегистрированный,
   отправить `"event": "connected"` (или промолчать — зависит от плагина, большинство не ждут ответа).

3. **`-info` JSON структура** (минимально необходимая):
   ```json
   {
     "application": {
       "font": "Arial",
       "language": "en",
       "platform": "windows",
       "platformVersion": "10.0.0",
       "version": "6.0.0.0"
     },
     "plugin": { "uuid": "<pluginId>", "version": "1.0.0" },
     "devicePixelRatio": 1,
     "colors": { ... },
     "devices": [
       {
         "id": "MK_DEVICE_0",
         "name": "MacroKeyboard",
         "size": { "columns": 4, "rows": 3 },
         "type": 0
       }
     ]
   }
   ```

**Файлы:** `PluginManager.cs` (`ExecutablePluginInstance`), `WebSocketServer.cs`

---

### Фаза 2 — Жизненный цикл (Easy, ~1 день)

После регистрации плагин ожидает lifecycle-события. Без них большинство плагинов
не переходит в рабочее состояние.

**События для реализации:**

| Событие | Когда отправлять |
|---|---|
| `deviceDidConnect` | При старте и при переподключении устройства |
| `deviceDidDisconnect` | При отключении устройства |
| `applicationDidLaunch` | Сразу после регистрации плагина |
| `systemDidWakeUp` | Опционально, при выходе системы из sleep |

**Пример `deviceDidConnect`:**
```json
{
  "event": "deviceDidConnect",
  "device": "MK_DEVICE_0",
  "deviceInfo": {
    "name": "MacroKeyboard",
    "type": 0,
    "size": { "columns": 4, "rows": 3 }
  }
}
```

**Файлы:** `WebSocketServer.cs`, `PluginManager.cs`

---

### Фаза 3 — Манифест в формате Stream Deck (Medium, ~1 день)

Реальный `manifest.json` Stream Deck плагина выглядит так:
```json
{
  "Name": "My Plugin",
  "Version": "1.0.0",
  "Author": "...",
  "SDKVersion": 2,
  "CodePath": "plugin.exe",
  "OS": [{ "Platform": "windows", "MinimumVersion": "10" }],
  "Software": { "MinimumVersion": "6.0" },
  "Actions": [
    {
      "Name": "My Action",
      "UUID": "com.myplugin.myaction",
      "Icon": "icons/action",
      "PropertyInspectorPath": "ui/config.html",
      "Tooltip": "...",
      "SupportedInMultiActions": true
    }
  ]
}
```

Наш `PluginManifest` не парсит большинство этих полей.

**Что нужно:**

1. **`PluginManifest.cs`** — расширить, добавить `SDKVersion`, `CodePath` (вместо нашего `Runtime`/`EntryPoint`),
   `OS`, `Software`, `Actions[].UUID` как основной идентификатор action (вместо `Id`).

2. **Обратная совместимость** — сохранить поддержку нашего формата (Managed плагины).
   Признак: наличие поля `"Type": "Managed"` → наш формат; иначе → Stream Deck формат.

3. **`ActionId` mapping** — для SD плагинов `ActionId` = UUID (`com.myplugin.myaction`),
   а не числовой `Id`.

**Файлы:** `MacroKeyboard.Core/Plugin/PluginManifest.cs`, `PluginManager.cs`

---

### Фаза 4 — Полный API событий (Medium, ~2 дня)

Stream Deck API имеет ~15 команд (от плагина к приложению) и ~10 событий (от приложения к плагину).
Сейчас реализованы только `keyDown`/`keyUp`. Чаще всего плагины используют:

**Команды от плагина (входящие, нужно обрабатывать):**

| Команда | Что делает | Статус |
|---|---|---|
| `setTitle` | Задать текст на кнопке | Частично (нет `state`, `target`) |
| `setImage` | Задать картинку (base64) | Частично |
| `showAlert` | Мигнуть восклицательным знаком | Частично (неправильный визуал) |
| `showOk` | Мигнуть галочкой | Не реализовано |
| `setState` | Переключить состояние multi-state action | Не реализовано |
| `setSettings` | Сохранить settings этого action instance | Частично |
| `getSettings` | Запросить settings | Не реализовано |
| `setGlobalSettings` | Глобальные настройки плагина | Не реализовано |
| `getGlobalSettings` | Запросить глобальные настройки | Не реализовано |
| `openUrl` | Открыть URL в браузере | Не реализовано |
| `logMessage` | Лог в консоль Stream Deck | Не реализовано |
| `sendToPropertyInspector` | Отправить данные в UI конфигурации | Не реализовано (нет PI) |

**События от приложения к плагину (исходящие):**

| Событие | Когда | Статус |
|---|---|---|
| `keyDown` | Нажатие кнопки | Реализовано |
| `keyUp` | Отпускание кнопки | Реализовано |
| `willAppear` | Кнопка появилась на экране (загружен профиль) | Не реализовано |
| `willDisappear` | Кнопка ушла (смена профиля) | Не реализовано |
| `didReceiveSettings` | Ответ на `getSettings` | Не реализовано |
| `didReceiveGlobalSettings` | Ответ на `getGlobalSettings` | Не реализовано |
| `titleParametersDidChange` | Пользователь изменил заголовок в UI | Не реализовано |
| `sendToPlugin` | Данные от Property Inspector | Не реализовано |

**Минимум для большинства плагинов:** `setTitle`, `setImage`, `getSettings`/`didReceiveSettings`,
`willAppear`, `openUrl`, `logMessage`.

**Файлы:** `WebSocketServer.cs`, `PluginManager.cs`

---

### Фаза 5 — Property Inspector (Hard, ~1 неделя)

Property Inspector — это HTML/JS страница из папки плагина, которую приложение отображает
в правой панели при выборе кнопки. Плагин и PI общаются через WebSocket.

**Почему HTML, а не нативный UI:**
- Плагины написаны на чём угодно (Node.js, Python, C++) — нет доступа к Avalonia
- HTML/JS — нейтральный формат, не зависит от языка плагина
- Elgato не может требовать от авторов плагинов учить Avalonia/WPF/SwiftUI

**Архитектура в оригинальном Stream Deck:**
```
[Plugin process] <-- WebSocket port 28196 --> [Stream Deck app]
                                                      |
                                               [WebView (PI HTML)]
                                                      |
                                         отдельный WS port или
                                         page.connectElgatoStreamDeckSocket()
```

**Что нужно в нашем случае:**

1. **WebView в Avalonia** — `Avalonia.Controls.WebView` v12.0.1 (официальный, stable май 2026).
   - Windows: WebView2 (предустановлен на Win 11)
   - macOS: WKWebView
   - `NativeWebView` контрол в AXAML, двусторонняя JS ↔ C# связь через `InvokeScript()` / `WebMessageReceived`
   - **Ограничение:** `file://` не поддерживается, нужен HTTP-сервер для локальных файлов

2. **Локальный HTTP-сервер** для раздачи HTML/CSS/JS из папки плагина.
   Плагины используют относительные пути к своим ресурсам, поэтому
   `PropertyInspectorServer.cs` обслуживает папку конкретного плагина на `localhost:<port>`.
   WebView открывает `http://localhost:<port>/PropertyInspectorPath` из манифеста.

3. **PI WebSocket канал** — отдельный порт (или тот же 28196 с routing по `uuid`),
   по которому PI-страница общается с плагином через команды `sendToPlugin`/`sendToPropertyInspector`.

4. **Интеграция с UI** — при открытии ButtonConfigDialog для Plugin Action загружать PI HTML
   вместо/рядом с полем "Settings JSON".

**Файлы (новые):** `PropertyInspectorServer.cs`, изменения в `ButtonConfigDialogViewModel.cs`,
`ProfileEditorView.axaml` (добавить WebView контрол).

---

## Приоритизация

Если цель — запустить реальный плагин без Property Inspector:

```
Фаза 1 (Запуск+регистрация) → Фаза 2 (Lifecycle) → Фаза 3 (Манифест) → Фаза 4 (API)
~2д                            ~1д                   ~1д                  ~2д
```

**Итого без PI: ~6-7 рабочих дней.**

Большинство "headless" плагинов (нет настроек или настройки только через файл) заработают
уже после Фаз 1-2. Фазы 3-4 нужны для плагинов с `getSettings` и сложной логикой.

Property Inspector — отдельное решение о scope. Без него плагины работают, но пользователь
не может настроить их через UI (только вручную редактировать JSON в поле Settings).

---

## Тестирование совместимости

Рекомендуемые плагины для теста (простые, без тяжёлого PI):
- **streamdeck-obs-ws** — OBS WebSocket интеграция, headless, только `keyDown`
- **Stream Deck Tools** — простые системные действия
- Любой плагин с открытым исходным кодом на GitHub (поиск `"stream deck plugin" site:github.com`)
