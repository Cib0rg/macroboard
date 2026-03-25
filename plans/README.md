# План разработки прошивки для ESP32-S3 макроклавиатуры

## Обзор проекта

Макроклавиатура на базе ESP32-S3 с 10 кнопками, каждая из которых имеет:
- Круглый дисплей GC9A01 (160×160 пикселей)
- RGB светодиод WS2812
- Программируемое действие (клавиатура или custom HID)

Дополнительно:
- Rotary encoder для переключения профилей
- WiFi OTA обновление
- USB HID (клавиатура + raw device + CDC)
- 3-5 профилей с сохранением в flash

## Структура документации

### 1. [Использование готовых компонентов ESP-IDF](esp_idf_components.md) ⭐ НОВОЕ

**Важный документ!** Показывает, что 80% функциональности уже есть в ESP-IDF:
- OTA в 10 строк кода с `esp_https_ota()`
- WiFi с готовым `esp_wifi` API
- NVS, SPIFFS, USB TinyUSB - всё готово
- Аппаратный JPEG декодер в ESP32-S3

Реально писать нужно только:
- GC9A01 driver (адаптировать из примеров)
- Display multiplexer (4 GPIO)
- Protocol handler (наш протокол)
- Profile manager (наша логика)
- Application glue

### 2. [Протокол обмена данными](protocol.md)

Детальное описание USB HID Raw протокола для связи с управляющим софтом:

**Основные команды:**
- `0x01` PING - проверка связи
- `0x02` GET_DEVICE_INFO - информация об устройстве
- `0x10` SET_PROFILE - переключение профиля
- `0x20-0x22` Передача изображений (START/CHUNK/END)
- `0x30` SET_BUTTON_ACTION - настройка действия кнопки
- `0x40` SET_LED_COLOR - настройка RGB LED
- `0x50-0x52` Управление профилями (SAVE/LOAD/DELETE)
- `0x60-0x61` OTA обновление (START/STATUS)
- `0x70-0x71` WiFi настройки

**Формат пакета:** 64 байта
- Magic byte (0xA5)
- Command ID
- Payload length
- Sequence number
- Payload (56 байт)
- Checksum (XOR)
- End byte (0x5A)

**Рекомендуемый формат изображений:** JPEG (8-15 КБ вместо 51 КБ для RGB565)

### 3. [Архитектура прошивки](architecture.md)

Модульная структура на базе ESP-IDF и FreeRTOS:

**Основные слои:**
- **Hardware Layer**: драйверы дисплеев, кнопок, энкодера, LED, SPI
- **USB Layer**: HID Keyboard, HID Raw, CDC
- **Protocol Layer**: обработка команд, передача изображений
- **Storage Layer**: NVS, SPIFFS, профили, изображения
- **Profile Layer**: управление профилями, выполнение действий
- **Network Layer**: WiFi, OTA, HTTP client
- **UI Layer**: отображение, декодирование JPEG, framebuffer
- **Utils Layer**: логирование, CRC, буферы, мониторинг

**FreeRTOS задачи:**
- USB RX Task (приоритет 20)
- Button Task (приоритет 18)
- Encoder Task (приоритет 18)
- Protocol Task (приоритет 15)
- Display Task (приоритет 12)
- LED Task (приоритет 10)
- WiFi Task (приоритет 8)
- OTA Task (приоритет 5)

**Использование памяти:**
- Flash: 16 МБ (3 МБ × 2 для OTA, 5 МБ для изображений, 1.5 МБ для профилей)
- SRAM: 512 КБ (200 КБ heap, 40 КБ stacks)
- PSRAM: 8 МБ (512 КБ framebuffers, 2 МБ cache)

### 4. [Структура хранения данных](storage.md)

**NVS (Non-Volatile Storage):**
- Device ID, текущий профиль, настройки
- WiFi credentials
- Калибровочные данные

**SPIFFS (6.9 МБ):**
```
/storage/
├── profiles/
│   ├── profile_0.bin (1.3 КБ)
│   ├── profile_1.bin
│   └── ...
└── images/
    ├── p0_b0.jpg (~10 КБ)
    ├── p0_b1.jpg
    └── ... (50 изображений всего)
```

**Формат файла профиля:**
- Заголовок (64 байта): magic, version, ID, name, timestamp, CRC32
- Кнопки (10 × 128 байт): action type, action data, LED config, image metadata

**Кэширование:**
- LRU cache для 5 декодированных изображений в PSRAM
- Предзагрузка при переключении профиля

### 5. [Диаграммы взаимодействия](system_flow.md)

**Процесс инициализации:**
1. Core Init (NVS, SPIFFS, Logger)
2. Hardware Init (SPI, Displays, Buttons, Encoder, LEDs)
3. USB Init (TinyUSB, HID, CDC)
4. Profile Loading (загрузка текущего профиля)
5. Display Init (инициализация и отрисовка всех дисплеев)
6. Task Creation (запуск всех FreeRTOS задач)
7. System Ready

**Обработка нажатия кнопки:** < 10 мс
```
GPIO Interrupt → Debounce → Get Action → Execute → USB HID → LED Flash
```

**Переключение профиля:** < 100 мс
```
Encoder Rotation → Profile Manager → Load Images → Update Displays → Update LEDs
```

**Передача изображения:**
```
START_IMAGE_TRANSFER → IMAGE_DATA_CHUNK (×N) → END_IMAGE_TRANSFER → Verify CRC → Save → Update Display
```

**WiFi OTA:**
```
SET_WIFI_CREDENTIALS → Connect → START_OTA → Download → Verify MD5 → Install → Reboot
```

### 6. [План имплементации](implementation_plan.md)

**Детальный код инициализации:**
- 7 фаз загрузки с проверками
- Создание профилей по умолчанию
- Обработка первого запуска

**Profile Manager:**
- Структуры данных профилей
- Функции переключения профилей
- Кэширование активного профиля

**Encoder Handler:**
- Накопление шагов (4 шага = 1 профиль)
- Циклическое переключение (0→1→2→3→4→0)
- Обработка кнопки энкодера

**WiFi OTA:**
- WiFi Manager с сохранением credentials
- OTA Task с загрузкой по HTTP
- MD5 верификация
- Rollback механизм при ошибке

### 7. [План тестирования](testing_plan.md)

**Unit Tests:**
- Protocol parser
- Profile storage
- Image cache

**Integration Tests:**
- USB communication
- Button actions
- Profile switching
- Image transfer

**Hardware Tests:**
- Displays (цветовые тесты)
- Buttons (последовательное нажатие)
- Encoder (вращение и кнопка)
- LEDs (rainbow эффект)

**Performance Tests:**
- Display update: < 50 мс
- Button latency: < 10 мс
- Memory usage: < 300 КБ SRAM

**Stress Tests:**
- Rapid profile switching (100 раз)
- Button spam (1000 нажатий)
- Long running (24 часа)

**Test Execution Plan:** 14 дней
- День 1-2: Unit tests
- День 3-5: Integration tests
- День 6-7: Hardware tests
- День 8-9: Performance tests
- День 10-12: Stress tests
- День 13-14: User acceptance testing

## Ключевые технические решения

### 1. Формат изображений: JPEG
**Обоснование:**
- Размер: 8-15 КБ вместо 51 КБ (RGB565)
- ESP32-S3 имеет аппаратный JPEG декодер
- Экономия flash: 500 КБ вместо 2.5 МБ для 50 изображений

### 2. USB HID Raw для управления
**Обоснование:**
- Не требует драйверов
- Работает на всех ОС
- Bidirectional communication
- 64-байтовые пакеты достаточны для команд

### 3. SPIFFS для хранения
**Обоснование:**
- Простая файловая система
- Wear leveling встроен
- Легко работать с файлами
- Не требует форматирования при обновлении

### 4. Кэширование в PSRAM
**Обоснование:**
- 8 МБ PSRAM достаточно для кэша
- Ускоряет переключение профилей
- Освобождает SRAM для критических данных

### 5. FreeRTOS задачи с приоритетами
**Обоснование:**
- Гарантирует отклик на пользовательский ввод
- Изолирует функциональность
- Упрощает отладку
- Позволяет параллельную обработку

## Производительность

### Целевые показатели

| Метрика | Цель | Текущая |
|---------|------|---------|
| Button latency | < 10 мс | TBD |
| Display update | < 50 мс | TBD |
| Profile switch | < 100 мс | TBD |
| Boot time | < 2 с | TBD |
| Memory usage | < 300 КБ | TBD |

### Оптимизации

1. **DMA для SPI** - освобождает CPU
2. **Аппаратный JPEG декодер** - ускорение в 10+ раз
3. **Двойная буферизация** - плавная отрисовка
4. **Кэширование изображений** - быстрое переключение
5. **Приоритеты задач** - гарантированный отклик

## Следующие шаги

### Фаза 1: Базовая функциональность (2-3 недели)
- [ ] Настройка ESP-IDF проекта
- [ ] Драйверы hardware (SPI, GPIO, RMT)
- [ ] USB HID базовая функциональность
- [ ] Тестирование на макетной плате

### Фаза 2: Протокол и хранение (2-3 недели)
- [ ] Реализация протокола обмена
- [ ] Storage layer (NVS, SPIFFS)
- [ ] Profile manager
- [ ] Передача изображений

### Фаза 3: Дисплеи и UI (2-3 недели)
- [ ] Драйвер GC9A01
- [ ] Display multiplexer
- [ ] JPEG декодер
- [ ] Анимации и эффекты

### Фаза 4: WiFi и OTA (1-2 недели)
- [ ] WiFi manager (используя esp_wifi API)
- [ ] OTA updater (используя esp_https_ota API)
- [ ] Rollback механизм (встроен в ESP-IDF)

### Фаза 5: Тестирование и отладка (2-3 недели)
- [ ] Unit tests
- [ ] Integration tests
- [ ] Hardware tests
- [ ] Performance optimization

### Фаза 6: Документация и релиз (1 неделя)
- [ ] User manual
- [ ] API documentation
- [ ] Release notes
- [ ] Production firmware

### Фаза 7: Использование готовых компонентов ESP-IDF
См. [`esp_idf_components.md`](esp_idf_components.md) для деталей о готовых компонентах.

**Общая оценка:** 6-8 недель (благодаря готовым компонентам ESP-IDF)

**Детальная оценка:**
- Неделя 1-2: Hardware drivers (GC9A01, multiplexer, buttons, encoder, LEDs)
- Неделя 3-4: Protocol handler + USB HID integration
- Неделя 5-6: Profile manager + application logic
- Неделя 7-8: Testing, debugging, optimization

## Инструменты разработки

### Необходимое ПО
- ESP-IDF v5.x
- VS Code с расширениями (ESP-IDF, C/C++)
- Git для версионирования
- Python 3.x для скриптов

### Аппаратура для разработки
- ESP32-S3-DevKitC-1 (N16R8)
- 10× GC9A01 дисплеи 160×160
- 10× Кнопки тактовые
- 10× WS2812 RGB LED
- 1× Rotary encoder с кнопкой
- Логический анализатор (для отладки SPI)
- USB кабель

### Полезные ресурсы
- [ESP-IDF Programming Guide](https://docs.espressif.com/projects/esp-idf/en/latest/)
- [TinyUSB Documentation](https://docs.tinyusb.org/)
- [GC9A01 Datasheet](https://www.buydisplay.com/download/ic/GC9A01A.pdf)
- [WS2812 Datasheet](https://cdn-shop.adafruit.com/datasheets/WS2812.pdf)

## Контакты и поддержка

Для вопросов по реализации обращайтесь к документации в директории `/plans`:
- [`esp_idf_components.md`](esp_idf_components.md) - готовые компоненты ESP-IDF ⭐
- [`protocol.md`](protocol.md) - протокол обмена
- [`architecture.md`](architecture.md) - архитектура
- [`storage.md`](storage.md) - хранение данных
- [`system_flow.md`](system_flow.md) - диаграммы
- [`implementation_plan.md`](implementation_plan.md) - детали реализации
- [`testing_plan.md`](testing_plan.md) - план тестирования

## Лицензия

TBD

---

**Версия документа:** 1.0  
**Дата:** 2026-03-25  
**Статус:** Готов к имплементации
