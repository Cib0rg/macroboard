# UI Refactor Plan

> **Sequencing note:** Implement `plugins.md` first. Phase 2 (Action Picker) and Phase 3
> (Config Panel) of this plan explicitly consume the IPC API and data models defined in
> `plugins.md`. Phases 1 and 4–5 of this plan are fully independent and can proceed in
> any order relative to plugins.

## Проблемы текущего UI (краткий список)

1. Список кнопок вместо визуальной сетки устройства
2. Три разных паттерна конфигурирования (inline-expand, сдвиг вверх, бейдж) для одного понятия
3. Inline-панель разрушает пространственный контекст — остальные кнопки уезжают за экран
4. «SAVE» означает три разные вещи (локальный JSON / кнопка в панели / отправка на устройство)
5. Статус-сообщение в левой панели при работе в центре
6. Long press бейдж виден на каждой строке, даже если не настроен
7. Drag-and-drop как основной жест — не discoverable
8. Левая панель — 8 кнопок без группировки, DELETE рядом с LOAD

---

## Новый Layout

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  MacroKeyboard  ·  [Profile: Work ▾]  [+ New]  [Duplicate]  [Delete]       │
│                                                               [→ Send]      │
├──────────────────────────────┬──────────────────────────────────────────────┤
│                              │                                              │
│   DEVICE CANVAS  (центр)     │   CONFIG PANEL  (правая)                    │
│                              │                                              │
│   ┌──────┐  ┌──────┐         │   ┌──────────────────────────────────────┐  │
│   │  🎵  │  │  📁  │  ···    │   │ Button 3                             │  │
│   │  B1  │  │  B2  │         │   ├──────────────────────────────────────┤  │
│   └──────┘  └──────┘         │   │ SHORT PRESS                          │  │
│                              │   │ Action: [ ⌨ Keyboard — Ctrl+C  ✎ ]  │  │
│   ┌──────┐  ┌──────┐         │   │                                      │  │
│   │      │  │  ⌨  │  ···    │   │   Key: [ Ctrl+C      ]  [Capture]   │  │
│   │  B3● │  │  B4  │         │   │   Or type: [                      ] │  │
│   └──────┘  └──────┘         │   ├──────────────────────────────────────┤  │
│                              │   │ LONG PRESS                           │  │
│    ···  (5 rows × 2 cols)    │   │ Action: [ — None —             ✎ ]  │  │
│                              │   ├──────────────────────────────────────┤  │
│  ┌─────────────────────┐     │   │ Button Name: [              ]        │  │
│  │  ↻ [Media: Vol+  ] │     │   │ Image:  [path...]  [📂]  [✕]        │  │
│  │  ↺ [None         ] │     │   │ LED:    ██  #FF4400   Brightness 80  │  │
│  │  ⏎ [Profile 2   ] │     │   └──────────────────────────────────────┘  │
│  │  ⏎⏎[None        ] │     │                                              │
│  └─────────────────────┘     │   (пусто, пока ничего не выбрано)          │
│                              │                                              │
├──────────────────────────────┴──────────────────────────────────────────────┤
│  ● Connected  ·  Profile "Work" saved  ·  Button 3 configured    [↺ Undo]  │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Легенда canvas:**
- `●` на кнопке B3 — выбрана (подсветка рамки)
- Иконка внутри — изображение кнопки (если задано) или буква
- Маленькая точка в углу кнопки — есть long press action
- Кнопки энкодера — отдельная компактная секция ниже сетки

---

## Ключевые решения

### 1. Device Canvas вместо списка

Центр экрана — визуальная копия устройства: сетка N×M кнопок.
Каждая кнопка рисуется как квадрат ~80×80px с:
- Изображением или инициалами action
- Полупрозрачным overlay с именем
- Индикатором long press (маленькая зелёная точка в правом нижнем углу)
- Hover-эффектом (подсветка рамки)
- Selected-состоянием (яркая рамка + активация правой панели)

Энкодер — полоска под сеткой с 4 слотами (↻ ↺ ⏎ ⏎⏎), кликабельная так же.

### 2. Action Picker вместо dropdown

**Проблема с dropdown:** при системе плагинов список action-типов растёт бесконечно.
`Keyboard | Media | Shell | Spotify | OBS | Home Assistant | ...` — ComboBox на 50+ позиций неюзабелен.

**Решение: Action Picker overlay** (паттерн Command Palette, как в VS Code / Raycast).

Поле Action в Config Panel — это **кнопка**, показывающая текущий action:
```
Action: [ ⌨  Keyboard — Ctrl+C                         ✎ ]
```
Клик на поле → открывается overlay поверх UI:

```
┌─────────────────────────────────────────┐
│  🔍  [search actions...              ]  │
├─────────────────────────────────────────┤
│  Recent                                 │
│    ⌨  Keyboard — Ctrl+C                │
│    🔊  Media — Volume Up               │
├─────────────────────────────────────────┤
│  ▾ Keyboard & Text                      │
│      ⌨  Key combination                │
│      📝  Type text                      │
├─────────────────────────────────────────┤
│  ▾ Media                                │
│      🔊  Volume Up / Down / Mute        │
├─────────────────────────────────────────┤
│  ▾ System                               │
│      🚀  Launch App                     │
│      💻  Shell Command                  │
├─────────────────────────────────────────┤
│  ▾ My Plugin: Spotify        [плагин]  │
│      ▶  Play/Pause                      │
│      ⏭  Next Track                      │
├─────────────────────────────────────────┤
│  ▾ My Plugin: OBS Studio     [плагин]  │
│      ⏺  Start Recording               │
└─────────────────────────────────────────┘
```

- **Fuzzy search** — «vol» → фильтрует до «Volume» из всех плагинов сразу
- **Recent** — последние использованные наверху
- **Категории сворачиваемые** — плагины не засоряют основной список
- **Клавиатурная навигация** — стрелки + Enter, Escape закрывает
- Drag-and-drop из отдельной правой палитры **убирается** — заменён picker'ом

**Реализация picker'а:**
- `ActionPickerViewModel` — список action-типов с категориями и fuzzy-поиском
- `ActionPickerOverlay.axaml` — Popup поверх основного UI
- `IActionTypeDescriptor` — UI-side абстракция для одного типа action:
  `{ string Category; string Name; string Icon; ActionConfig CreateDefault(); }`
- `ActionTypeRegistry` строится из двух источников:
  1. Встроенные типы: hard-coded `IActionTypeDescriptor` реализации для каждого `ActionType` enum значения
  2. Плагины: `PluginActionInfo` объекты из IPC `plugin.list` (из `plugins.md` Фаза 5), обёрнутые в `PluginActionDescriptor : IActionTypeDescriptor`
- Picker строится динамически из реестра — не требует изменений при добавлении плагина

> **Зависимость от plugins.md:** для отображения плагинов в Picker'е требуется реализованный
> IPC `plugin.list` из `plugins.md` Фаза 5. Без него Picker работает только со встроенными
> типами — плагины появятся автоматически после внедрения plugins.md.

### 3. Единая правая Config Panel

Одна панель справа, которая меняет содержимое в зависимости от выбранного элемента.
Без inline-разворачивания, без сдвига контента.

Структура панели:
```
[Заголовок: Button N]
─────────────────────────────────
SHORT PRESS
Action: [ ⌨  Keyboard — Ctrl+C  ✎ ]   ← кнопка → ActionPicker
[параметры для выбранного типа]

─────────────────────────────────
LONG PRESS
Action: [ — None —               ✎ ]
[параметры, если не None]

─────────────────────────────────
Button Name: [TextBox]
Image:       [path + browse + clear]
LED:         [color picker + brightness]
```

Short press и long press — в ОДНОЙ панели, разделены separator'ом.
Нет двух отдельных диалогов — всё видно сразу.

Для энкодера — та же панель, без Image/LED/LongPress.

> **Зависимость от plugins.md:** Config Panel должна включать секцию Plugin action
> (`PluginId` read-only + `Settings` JSON textarea), аналогичную той, что добавляется
> в `ButtonConfigDialogViewModel` в `plugins.md` Фаза 6. Реализовывать Panel до plugins.md
> означает добавлять plugin-секцию дважды. Предпочтительнее: сначала plugins.md, затем этот рефактор.

### 4. Разделение Save / Send

Два визуально разных действия в топбаре:
- **💾 Save** (серый/нейтральный) — сохранить в локальный JSON, быстро, всегда доступно
- **→ Send to Device** (зелёный, акцентный) — передать на железку, показывает прогресс

Кнопка Send имеет состояния: нормальное / sending (spinner) / success (галочка 2с) / error (красная).

Auto-save в JSON при каждом изменении в Config Panel (без явного нажатия Save).
«Save» тогда становится «Save as...» / export — можно убрать из главного toolbar вообще.

### 5. Статус-бар внизу

Единая строка статуса во всю ширину:
```
● Connected  ·  Last saved: 14:32  ·  Button 3: Keyboard Ctrl+C    [↺ Undo]
```

- Зелёная точка / серая — статус подключения устройства
- Сообщения об операциях — появляются здесь, не в левой панели
- Undo (если реализовывать) — здесь

### 6. Профили в топбаре

Dropdown с текущим профилем + [+ New] [Duplicate] [Delete] — компактно, в одну строку.
Список профилей выходит при клике на dropdown.
Убирает нужду в отдельной левой панели под список профилей.

---

## Что убирается

| Было | Становится |
|------|-----------|
| Левая панель с 8 кнопками | Topbar: profile dropdown + 3 кнопки |
| 3 паттерна конфига (inline/overlay/badge) | Единая правая панель |
| Inline-expand под строкой | Нет — правая панель всегда на месте |
| Long press бейдж на каждой строке | Точка-индикатор на canvas-кнопке |
| Статус в левой панели | Статус-бар внизу |
| Drag-and-drop как первичный жест | Клик на поле action → Action Picker overlay (drag убирается) |
| ComboBox типа action (не масштабируется) | Action Picker с fuzzy-search и категориями плагинов |
| SAVE кнопка в Config Panel | Auto-save при изменении |
| Синтетический ButtonId+100 механизм | Long press в основной панели напрямую |

---

## Поэтапная реализация

### Фаза 1 — Device Canvas (независима, самое ценное)

**Файлы:**
- Новый `DeviceCanvasView.axaml` + `DeviceCanvasView.axaml.cs`
- Новый `DeviceCanvasViewModel.cs` (ObservableCollection кнопок, выбранная кнопка)
- `ProfileEditorView.axaml` — заменить ItemsControl-список на DeviceCanvasView

**Что делает:**
- Рисует кнопки как UniformGrid N×M
- Каждая кнопка — `ButtonTileView` с изображением, оверлеем текста, индикатором long press
- Клик на кнопку — выставляет SelectedButton в ProfileEditorViewModel

**Сложность:** средняя. Не трогает Config Panel, не трогает бэкенд.

---

### Фаза 2 — Action Picker

Делается до Config Panel refactor, потому что Config Panel будет на него опираться.
Можно начать до завершения plugins.md — Picker заработает со встроенными типами сразу,
плагины подтянутся автоматически после выполнения plugins.md Фаза 5.

**Файлы:**
- Новый `IActionTypeDescriptor.cs` (Core) — интерфейс регистрации типов действий
- Новый `ActionTypeRegistry.cs` (Core) — реестр, принимает встроенные дескрипторы + `PluginActionInfo` из IPC
- Новый `ActionPickerViewModel.cs` — список категорий, fuzzy-фильтр, Recent
- Новый `ActionPickerOverlay.axaml` — Popup с поиском и деревом категорий

**Что делает:**
- Заменяет `SelectedActionType` ComboBox в `ButtonConfigDialogViewModel`
- Поле типа action становится кнопкой, открывающей Picker
- Поиск работает по имени и категории, включая плагины
- Recent хранится в `AppSettings` (последние 8 использованных)

**Сложность:** средняя. Изолированный компонент, не трогает layout.

---

### Фаза 3 — Единая Config Panel

> **Предпочтительный порядок:** реализовать после plugins.md, чтобы включить plugin-секцию
> с первого раза, а не добавлять её отдельным патчем.

**Файлы:**
- Новый `ButtonConfigPanelView.axaml` (UserControl)
- Рефактор `ButtonConfigDialogViewModel` → новый `ButtonConfigPanelViewModel`

**Что делает:**
- Short press и long press в одной панели — один `ButtonConfigPanelView`
- Убирает синтетический ButtonId+100 механизм и `IsLongPress` флаг
- Включает Plugin action секцию (`PluginId` read-only + JSON textarea), аналогичную той что в plugins.md Фаза 6
- Config Panel показывается справа всегда, меняет DataContext при смене выбранной кнопки

**Сложность:** высокая. Затрагивает `ButtonConfigDialogViewModel`, `ProfileEditorViewModel`, весь layout.

---

### Фаза 4 — Topbar профилей + Статус-бар

**Файлы:**
- `ProfileEditorView.axaml` — добавить RowDefinitions, новые строки topbar и statusbar
- `ProfileEditorViewModel` — добавить `StatusBarMessage`, убрать `StatusMessage` из левой панели

**Что делает:**
- Профили — dropdown в топбаре
- Статус — нижняя строка

**Сложность:** низкая. Чистый layout, не трогает логику.

---

### Фаза 5 — Разделение Save / Send + Auto-save

**Что делает:**
- Auto-save при каждом изменении Config Panel (`PropertyChanged` → debounce 500ms → `_profileService.UpdateProfileAsync`)
- Send button = отдельная, зелёная, с progress
- Убрать SAVE из Config Panel

**Сложность:** низкая-средняя.

---

## Приоритет и порядок

```
plugins.md (все фазы)         ← начинать здесь
    │
    ├── ui_refactor Фаза 1 (Device Canvas)  — параллельно с plugins.md, полностью независима
    ├── ui_refactor Фаза 2 (Action Picker)  — параллельно; плагины в Picker появятся позже
    ├── ui_refactor Фаза 3 (Config Panel)   — после plugins.md Фаза 6
    ├── ui_refactor Фаза 4 (Topbar/Status)  — независима
    └── ui_refactor Фаза 5 (Save/Send)      — независима
```

Фаза 1 даёт 80% пользы за разумные усилия.
Фазы 2–5 можно делать независимо и в любом порядке после Фазы 1.
