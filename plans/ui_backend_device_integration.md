# Задача: Полная интеграция UI ↔ Backend ↔ Устройство

**Дата создания:** 2026-04-21  
**Приоритет:** Высокий
**Статус:** В процессе (Phase 1 завершена)

---

## Контекст

Прошивка переведена на USB Composite Device (HID Keyboard + Vendor). Backend переведён с HidSharp на libusb P/Invoke. Базовая связь PC ↔ устройство работает. Исправлены race conditions и JSON десериализация IPC.

Нужна полная ревизия и доработка интеграции всех трёх слоёв, чтобы UI полноценно управлял устройством.

---

## Чеклист

### 1. UI ↔ Backend (IPC)

- [x] Dashboard корректно отображает: Device name, FW version, Profile, Status
- [x] Profile Editor: кнопка Save работает (сохраняет профиль через IPC → Backend → Device)
- [x] Profile Editor: добавить кнопку Load (загрузка профиля с устройства)
- [x] Button Config: настройка действий кнопок отправляется на устройство
- [x] LED Config: настройка цветов отправляется на устройство
- [x] Events: кнопки и энкодер отображаются в реальном времени в Dashboard
- [x] Settings view: проверить работоспособность (без изменений, работает)
- [x] Все IPC-сообщения корректно десериализуются (JObject → типизированные объекты)

### 2. Backend ↔ Device (USB Vendor)

- [x] `CMD_GET_DEVICE_INFO` (0x02) — корректный парсинг ответа
- [x] `CMD_SET_PROFILE` (0x10) — переключение профиля
- [x] `CMD_GET_PROFILE_INFO` (0x11) — получение информации о профиле
- [x] `CMD_SET_BUTTON_ACTION` (0x30) — настройка действия кнопки
- [x] `CMD_GET_BUTTON_ACTION` (0x31) — чтение действия кнопки
- [x] `CMD_SET_LED_COLOR` (0x40) — настройка цвета LED
- [x] `CMD_GET_LED_COLOR` (0x42) — чтение цвета LED
- [x] `CMD_SAVE_PROFILE` (0x50) — сохранение профиля в NVS
- [x] Image Transfer: `CMD_START_IMAGE_TRANSFER` → `CMD_IMAGE_DATA_CHUNK` → `CMD_END_IMAGE_TRANSFER`
- [x] Events: `EVENT_BUTTON_PRESSED` (0xF0), `EVENT_ENCODER_ROTATED` (0xF1), `EVENT_PROFILE_CHANGED` (0xF3)

### 3. Сквозные сценарии

- [x] Нажатие кнопки на устройстве → событие в UI Dashboard
- [x] Поворот энкодера → смена профиля → обновление UI
- [x] Изменение профиля через UI → отправка на устройство → подтверждение
- [x] Загрузка изображения через UI → передача на устройство → отображение на дисплее
- [x] Настройка LED через UI → изменение цвета на устройстве
- [x] Настройка действия кнопки через UI → сохранение на устройстве

---

## Ключевые файлы

### Прошивка
| Файл | Описание |
|------|----------|
| `firmware/main/protocol/protocol_handler.c` | Обработка команд от PC |
| `firmware/main/protocol/protocol_types.h` | Типы команд и событий |
| `firmware/main/usb/usb_vendor.c` | USB Vendor интерфейс |
| `firmware/main/usb/usb_descriptors.c` | USB дескрипторы |

### Backend
| Файл | Описание |
|------|----------|
| `software/src/MacroKeyboard.Backend/Services/DeviceManager.cs` | Управление подключением устройства |
| `software/src/MacroKeyboard.Backend/Services/EventRouter.cs` | Маршрутизация событий Device → IPC |
| `software/src/MacroKeyboard.Backend/Services/IpcServer.cs` | IPC сервер (TCP) |

### Communication
| Файл | Описание |
|------|----------|
| `software/src/MacroKeyboard.Communication/HidDevice/HidDeviceManager.cs` | USB Vendor через libusb |
| `software/src/MacroKeyboard.Communication/Protocol/ProtocolHandler.cs` | Протокол команд |
| `software/src/MacroKeyboard.Communication/Protocol/ProtocolConstants.cs` | Константы протокола |
| `software/src/MacroKeyboard.Communication/Usb/LibUsb.cs` | P/Invoke для libusb-1.0 |

### Infrastructure
| Файл | Описание |
|------|----------|
| `software/src/MacroKeyboard.Infrastructure/Services/DeviceService.cs` | Сервис устройства |
| `software/src/MacroKeyboard.Infrastructure/Services/ProfileService.cs` | Сервис профилей |

### UI
| Файл | Описание |
|------|----------|
| `software/src/MacroKeyboard.UI/ViewModels/DashboardViewModel.cs` | Dashboard |
| `software/src/MacroKeyboard.UI/ViewModels/ProfileEditorViewModel.cs` | Редактор профилей |
| `software/src/MacroKeyboard.UI/ViewModels/MainWindowViewModel.cs` | Главное окно |

### IPC
| Файл | Описание |
|------|----------|
| `software/src/MacroKeyboard.Shared/IPC/IpcClient.cs` | IPC клиент |
| `software/src/MacroKeyboard.Shared/IPC/IpcMessage.cs` | Типы IPC сообщений |
| `software/src/MacroKeyboard.Shared/Events/DeviceEventArgs.cs` | Типы событий |

---

## Известные проблемы

1. **Имена USB-интерфейсов в ОС** — Vendor-интерфейс показывается как "Vendor Interface", HID как "USB устройство ввода". Требуется кастомный драйвер/INF-файл.
2. **`GetDeviceInfoAsync` может не парсить ответ** — нужно проверить формат ответа прошивки vs ожидания Backend.
3. **Profile Editor не имеет кнопки Load** — только Save.

---

## Архитектура потока данных

```
┌──────────┐     IPC (TCP)     ┌──────────┐    USB Vendor    ┌──────────┐
│    UI    │ ◄──────────────► │  Backend  │ ◄──────────────► │  ESP32   │
│ Avalonia │   JSON messages   │   C# .NET │   64-byte pkts   │ Firmware │
└──────────┘                   └──────────┘                   └──────────┘

UI → Backend: IpcMessage { MessageType, Data }
Backend → Device: ProtocolPacket { Magic, CommandId, Payload[56], Checksum, End }
Device → Backend: ProtocolPacket (response or event)
Backend → UI: IpcMessage { MessageType, Data }
```

---

## Оценка трудозатрат

| Задача | Оценка |
|--------|--------|
| Ревизия IPC десериализации | 0.5 дня |
| Ревизия Backend ↔ Device команд | 1-2 дня |
| Profile Editor (Save/Load) | 1 день |
| Button/LED Config через UI | 1 день |
| Image Transfer | 1-2 дня |
| Сквозное тестирование | 1 день |
| **Итого** | **5-7 дней** |
