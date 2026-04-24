# Экшен "Папка" (Folder Action) — Спецификация

**Дата:** 2026-04-23
**Статус:** Реализовано (кроме UI-редактора папок)

---

## Концепция

Экшен `ACTION_TYPE_FOLDER` (`0x04`) реализует **навигацию по папкам** — аналог папок в Elgato Stream Deck. Кнопка с этим экшеном работает как **toggle**: первое нажатие входит в папку, повторное нажатие той же кнопки — выходит обратно.

---

## Структура данных

Каждый профиль (`profile_t` в `firmware/main/profile/profile_types.h:75`) содержит:
- `buttons[10]` — 10 кнопок корневого уровня
- `folders[16]` — до **16 папок**, каждая со своими `buttons[10]`

```
profile_t
├── buttons[0..9]              ← корневой уровень (видно по умолчанию)
├── folders[0]
│   ├── name[32]
│   └── buttons[0..9]          ← кнопки внутри папки 0
├── folders[1]
│   └── buttons[0..9]          ← кнопки внутри папки 1
├── ...
└── folders[15]
    └── buttons[0..9]          ← кнопки внутри папки 15
```

Конфигурация (`firmware/main/config.h`):
```c
#define NUM_FOLDERS         16   // Максимум папок на профиль
#define FOLDER_STACK_DEPTH  4    // Максимальная глубина вложенности
```

---

## Алгоритм работы

### Вход/выход из папки (`firmware/main/profile/action_executor.c:72-91`)

```
Нажатие кнопки с ACTION_TYPE_FOLDER
  │
  ├─ Если мы УЖЕ в папке И ЭТА ЖЕ кнопка нас сюда привела:
  │     → Выход из папки (profile_folder_exit)
  │     → folder_entry_button_id = 0xFF
  │
  └─ Иначе:
        → Вход в папку (profile_folder_enter(folder_id))
        → folder_entry_button_id = button_id
```

### Навигация по стеку (`firmware/main/profile/profile_manager.c`)

Папки реализованы через **стек**:
```c
static uint8_t folder_stack[FOLDER_STACK_DEPTH];  // [4] элемента
static uint8_t folder_stack_depth = 0;
```

| Функция | Описание |
|---------|----------|
| `profile_folder_enter(folder_id)` | Push folder_id в стек, обновляет LED из кнопок папки |
| `profile_folder_exit()` | Pop из стека, восстанавливает LED родителя |
| `profile_get_button_config(button_id)` | Если в папке → кнопки папки, иначе → кнопки корня |
| `profile_is_in_folder()` | true если folder_stack_depth > 0 |
| `profile_get_folder_depth()` | Текущая глубина вложенности |
| `profile_get_current_folder()` | ID текущей папки (0xFF если корень) |

### Что происходит при входе/выходе

| Действие | LED | Дисплеи | Кнопки |
|----------|-----|---------|--------|
| Вход в папку | ✅ Обновляются из `folder.buttons[i].led_*` | ❌ Не реализовано | ✅ `profile_get_button_config()` возвращает кнопки папки |
| Выход из папки | ✅ Восстанавливаются из родителя | ❌ Не реализовано | ✅ `profile_get_button_config()` возвращает кнопки родителя |

---

## Максимальная вложенность: 4 уровня

Кнопка внутри папки тоже может иметь `ACTION_TYPE_FOLDER`, создавая вложенную навигацию:

```
Root → Folder A → Folder B → Folder C → Folder D (максимум)
  0        1          2          3          4 (FOLDER_STACK_DEPTH)
```

При попытке войти на 5-й уровень `profile_folder_enter()` вернёт `ESP_ERR_NO_MEM`.

---

## Ключевые файлы

| Файл | Описание |
|------|----------|
| `firmware/main/profile/profile_types.h` | Структуры `profile_t`, `folder_t`, `button_config_t` |
| `firmware/main/profile/profile_manager.c` | Стек папок, enter/exit, get_button_config |
| `firmware/main/profile/action_executor.c` | Toggle-логика входа/выхода |
| `firmware/main/config.h` | `NUM_FOLDERS=16`, `FOLDER_STACK_DEPTH=4` |
| `software/src/MacroKeyboard.Core/Models/Profile.cs` | PC-модель профиля (List<Folder>) |
| `software/src/MacroKeyboard.Core/Models/ActionType.cs` | `Folder = 0x04` |

---

## Ограничения текущей реализации

1. ~~**Дисплеи не обновляются**~~ ✅ Исправлено — `profile_update_button_display()` обновляет дисплеи (LED-цвет как fallback, JPEG decode — TODO)
2. **Выход только по той же кнопке** — это by design (toggle), не баг
3. ~~**UI не поддерживает редактирование папок**~~ ✅ Исправлено — Profile Editor показывает вложенный список кнопок с отступами для каждого уровня папки. При назначении `ActionType.Folder` кнопке, папка автоматически создаётся с 10 пустыми кнопками.
4. ~~**Нет визуальной индикации**~~ ✅ Исправлено — Dashboard показывает "Folder X (depth: Y)" + 📁 иконку
   - TODO: Надо, чтобы картинка на кнопке-входе в папку менялась на строго заданную и вкомпиленную в прошивку. Создать отдельную директорию для таких ассетов в firmware.
5. ~~**PC-сторона не знает о текущей папке**~~ ✅ Исправлено — добавлены EVENT_FOLDER_ENTERED/EXITED (0xF5/0xF6), IPC "folder.entered"/"folder.exited"

---

## Ассеты прошивки (изображения для дисплеев)

### Расположение

Изображения для кнопок хранятся в SPIFFS-разделе прошивки по пути:

```
/storage/img_{profile_id}_{button_id}.jpg
```

Формат задан в `firmware/main/config.h`:
```c
#define IMAGE_FILE_FMT  "/storage/img_%d_%d.jpg"
```

### Именование файлов

| Файл | Описание |
|------|----------|
| `/storage/img_0_0.jpg` | Профиль 0, кнопка 0 |
| `/storage/img_0_1.jpg` | Профиль 0, кнопка 1 |
| `/storage/img_0_9.jpg` | Профиль 0, кнопка 9 |
| `/storage/img_1_0.jpg` | Профиль 1, кнопка 0 |
| ... | ... |
| `/storage/img_4_9.jpg` | Профиль 4, кнопка 9 |

### Формат изображений

- **Формат:** JPEG
- **Размер дисплея:** 160×160 пикселей (GC9A01 round LCD)
- **Рекомендуемый размер файла:** < 20 KB (ограничение SPIFFS)
- **Цветовое пространство:** RGB (конвертируется в RGB565 при отображении)

### Встроенные ассеты (TODO)

Для системных иконок (папка, назад, и т.д.) планируется создать директорию:

```
firmware/main/assets/
├── folder_icon.jpg      — иконка папки (показывается на кнопке с ACTION_TYPE_FOLDER)
├── back_icon.jpg        — иконка "назад" (для будущего использования)
└── empty_icon.jpg       — пустая кнопка
```

Эти файлы будут вкомпилированы в прошивку через `EMBED_FILES` в CMakeLists.txt и доступны как `extern const uint8_t folder_icon_start[] asm("_binary_folder_icon_jpg_start")`.

### Загрузка изображений с PC

Изображения загружаются на устройство через протокол Image Transfer:
1. `CMD_START_IMAGE_TRANSFER` (0x20) — начало передачи (profile_id, button_id, size, format)
2. `CMD_IMAGE_DATA_CHUNK` (0x21) — фрагмент данных (до 50 байт на пакет)
3. `CMD_END_IMAGE_TRANSFER` (0x22) — завершение с проверкой CRC32

Реализация на PC: `software/src/MacroKeyboard.Communication/Commands/ImageTransferCommand.cs`
Реализация в прошивке: `firmware/main/protocol/image_transfer.c`
