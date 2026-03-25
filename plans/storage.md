# Структура хранения данных в энергонезависимой памяти

## Обзор

ESP32-S3 N16R8 имеет 16 МБ flash памяти, которая используется для:
- Прошивки (2 копии для OTA)
- Профилей клавиатуры
- Изображений для дисплеев
- Конфигурации устройства
- WiFi credentials

## Разметка Flash памяти

### Partition Table (partitions.csv)

```csv
# Name,     Type, SubType,  Offset,   Size,     Flags
nvs,        data, nvs,      0x9000,   0x4000,
otadata,    data, ota,      0xd000,   0x2000,
phy_init,   data, phy,      0xf000,   0x1000,
factory,    app,  factory,  0x10000,  0x300000,
ota_0,      app,  ota_0,    0x310000, 0x300000,
ota_1,      app,  ota_1,    0x610000, 0x300000,
storage,    data, spiffs,   0x910000, 0x6F0000,
```

### Детальная разметка

```
┌─────────────────────────────────────────────────────────┐
│ 0x000000 - 0x00FFFF : Bootloader (64 KB)                │
├─────────────────────────────────────────────────────────┤
│ 0x008000 - 0x008FFF : Partition Table (4 KB)            │
├─────────────────────────────────────────────────────────┤
│ 0x009000 - 0x00CFFF : NVS (16 KB)                       │
│   - WiFi credentials                                     │
│   - Device settings                                      │
│   - Current profile ID                                   │
│   - Calibration data                                     │
├─────────────────────────────────────────────────────────┤
│ 0x00D000 - 0x00EFFF : OTA Data (8 KB)                   │
│   - Boot partition selector                              │
│   - OTA state                                            │
├─────────────────────────────────────────────────────────┤
│ 0x00F000 - 0x00FFFF : PHY Init (4 KB)                   │
│   - WiFi PHY calibration                                 │
├─────────────────────────────────────────────────────────┤
│ 0x010000 - 0x30FFFF : Factory App (3 MB)                │
│   - Заводская прошивка (fallback)                       │
├─────────────────────────────────────────────────────────┤
│ 0x310000 - 0x60FFFF : OTA_0 (3 MB)                      │
│   - Первая копия прошивки для OTA                       │
├─────────────────────────────────────────────────────────┤
│ 0x610000 - 0x90FFFF : OTA_1 (3 MB)                      │
│   - Вторая копия прошивки для OTA                       │
├─────────────────────────────────────────────────────────┤
│ 0x910000 - 0xFFFFFF : Storage (6.9 MB)                  │
│   ├── Profiles (1.5 MB)                                  │
│   ├── Images (5 MB)                                      │
│   └── Logs/Reserved (0.4 MB)                             │
└─────────────────────────────────────────────────────────┘
```

## NVS (Non-Volatile Storage)

### Namespace: "config"

Хранит основные настройки устройства.

```c
// Ключи NVS
#define NVS_KEY_DEVICE_ID       "device_id"      // UUID устройства (16 bytes)
#define NVS_KEY_CURRENT_PROFILE "curr_profile"   // Текущий профиль (uint8_t)
#define NVS_KEY_BRIGHTNESS      "brightness"     // Яркость дисплеев (uint8_t)
#define NVS_KEY_LED_BRIGHTNESS  "led_bright"     // Яркость LED (uint8_t)
#define NVS_KEY_LOG_LEVEL       "log_level"      // Уровень логирования (uint8_t)
#define NVS_KEY_LOG_ENABLED     "log_enabled"    // Логирование вкл/выкл (uint8_t)
#define NVS_KEY_FIRST_BOOT      "first_boot"     // Флаг первого запуска (uint8_t)
```

### Namespace: "wifi"

Хранит WiFi credentials для OTA.

```c
#define NVS_KEY_WIFI_SSID       "ssid"           // SSID (string, max 32)
#define NVS_KEY_WIFI_PASSWORD   "password"       // Password (string, max 64)
#define NVS_KEY_WIFI_ENABLED    "wifi_enabled"   // WiFi включен (uint8_t)
```

### Namespace: "calibration"

Калибровочные данные (если потребуется).

```c
#define NVS_KEY_ENCODER_STEPS   "enc_steps"      // Шагов на детент (uint8_t)
#define NVS_KEY_BUTTON_DEBOUNCE "btn_debounce"   // Время debounce (uint16_t, ms)
```

## SPIFFS Storage Partition

Используется файловая система SPIFFS для хранения профилей и изображений.

### Структура директорий

```
/storage/
├── profiles/
│   ├── profile_0.bin
│   ├── profile_1.bin
│   ├── profile_2.bin
│   ├── profile_3.bin
│   └── profile_4.bin
│
├── images/
│   ├── p0_b0.jpg    (profile 0, button 0)
│   ├── p0_b1.jpg
│   ├── ...
│   ├── p0_b9.jpg
│   ├── p1_b0.jpg
│   ├── ...
│   └── p4_b9.jpg    (50 изображений всего)
│
└── cache/
    ├── decoded_0.raw  (декодированные изображения в RGB565)
    └── ...
```

## Формат файла профиля

### Структура profile_X.bin

```c
// Заголовок профиля (64 байта)
typedef struct __attribute__((packed)) {
    uint32_t magic;              // 0x50524F46 ("PROF")
    uint16_t version;            // Версия формата (текущая: 1)
    uint8_t profile_id;          // ID профиля (0-4)
    uint8_t flags;               // Флаги (configured, etc.)
    char name[32];               // Имя профиля (UTF-8, null-terminated)
    uint32_t timestamp;          // Unix timestamp создания
    uint32_t data_size;          // Размер данных после заголовка
    uint32_t crc32;              // CRC32 всего файла (кроме этого поля)
    uint8_t reserved[12];        // Резерв
} profile_header_t;

// Конфигурация кнопки (128 байт)
typedef struct __attribute__((packed)) {
    uint8_t button_id;           // ID кнопки (0-9)
    uint8_t action_type;         // Тип действия (0x01=Keyboard, 0x02=Custom HID)
    uint16_t action_data_len;    // Длина данных действия
    uint8_t action_data[100];    // Данные действия
    
    // LED конфигурация
    uint8_t led_r;               // Red (0-255)
    uint8_t led_g;               // Green (0-255)
    uint8_t led_b;               // Blue (0-255)
    uint8_t led_brightness;      // Яркость (0-255)
    uint8_t led_effect;          // Эффект (0=Static, 1=Breathing, 2=Rainbow)
    
    // Метаданные изображения
    uint32_t image_offset;       // Смещение в файле изображения (или 0 если внешний файл)
    uint32_t image_size;         // Размер изображения
    uint8_t image_format;        // Формат (0x01=JPEG, 0x02=RGB565)
    
    uint8_t reserved[10];        // Резерв
} button_config_t;

// Полная структура файла профиля
typedef struct {
    profile_header_t header;
    button_config_t buttons[10];  // 10 кнопок × 128 байт = 1280 байт
    // Опционально: встроенные изображения (если image_offset != 0)
} profile_file_t;
```

### Размер файла профиля

- Заголовок: 64 байта
- Кнопки: 10 × 128 = 1280 байт
- **Итого**: 1344 байта (без встроенных изображений)

Если изображения встроены в файл профиля:
- 10 изображений × ~10 КБ = ~100 КБ
- **Итого**: ~101 КБ на профиль

**Рекомендация**: Хранить изображения отдельно для экономии места и упрощения обновления.

## Формат файла изображения

### Имя файла

```
pX_bY.jpg
где X = profile_id (0-4)
    Y = button_id (0-9)
```

### Формат

**JPEG** (рекомендуемый):
- Размер: 160×160 пикселей
- Качество: 80-90%
- Цветовое пространство: RGB
- Средний размер: 8-15 КБ

**RGB565 raw** (альтернатива):
- Размер: 160×160 пикселей × 2 байта = 51,200 байт
- Формат: RGB565 little-endian
- Без сжатия

### Метаданные изображения

Хранятся в профиле (button_config_t):
- Размер файла
- Формат
- CRC32 (для проверки целостности)

## Кэширование

### Decoded Image Cache

Для ускорения отрисовки декодированные изображения кэшируются в PSRAM.

```c
typedef struct {
    uint8_t profile_id;
    uint8_t button_id;
    uint16_t width;
    uint16_t height;
    uint32_t size;              // Размер в байтах
    uint8_t* data;              // RGB565 данные в PSRAM
    uint32_t last_access;       // Timestamp последнего доступа
    bool valid;                 // Флаг валидности
} image_cache_entry_t;

#define IMAGE_CACHE_SIZE 5      // Кэшировать 5 последних изображений
```

### LRU (Least Recently Used) политика

При нехватке памяти удаляются наименее используемые изображения.

## Операции с хранилищем

### Инициализация

```c
esp_err_t storage_init(void) {
    // 1. Инициализация NVS
    esp_err_t ret = nvs_flash_init();
    if (ret == ESP_ERR_NVS_NO_FREE_PAGES || 
        ret == ESP_ERR_NVS_NEW_VERSION_FOUND) {
        ESP_ERROR_CHECK(nvs_flash_erase());
        ret = nvs_flash_init();
    }
    
    // 2. Монтирование SPIFFS
    esp_vfs_spiffs_conf_t conf = {
        .base_path = "/storage",
        .partition_label = "storage",
        .max_files = 10,
        .format_if_mount_failed = true
    };
    ret = esp_vfs_spiffs_register(&conf);
    
    // 3. Проверка целостности
    size_t total = 0, used = 0;
    ret = esp_spiffs_info("storage", &total, &used);
    LOG_INFO("SPIFFS", "Total: %d KB, Used: %d KB", total/1024, used/1024);
    
    return ret;
}
```

### Сохранение профиля

```c
esp_err_t profile_save(uint8_t profile_id, const profile_t* profile) {
    char path[32];
    snprintf(path, sizeof(path), "/storage/profiles/profile_%d.bin", profile_id);
    
    // 1. Подготовка данных
    profile_file_t file_data;
    memset(&file_data, 0, sizeof(file_data));
    
    // Заполнение заголовка
    file_data.header.magic = 0x50524F46;
    file_data.header.version = 1;
    file_data.header.profile_id = profile_id;
    file_data.header.timestamp = time(NULL);
    strncpy(file_data.header.name, profile->name, 31);
    
    // Копирование конфигурации кнопок
    memcpy(file_data.buttons, profile->buttons, sizeof(file_data.buttons));
    
    // Расчет CRC32
    file_data.header.crc32 = crc32_calculate(&file_data, 
        sizeof(file_data) - sizeof(uint32_t));
    
    // 2. Запись в файл
    FILE* f = fopen(path, "wb");
    if (!f) return ESP_FAIL;
    
    size_t written = fwrite(&file_data, 1, sizeof(file_data), f);
    fclose(f);
    
    // 3. Обновление NVS
    nvs_handle_t nvs;
    nvs_open("config", NVS_READWRITE, &nvs);
    nvs_set_u8(nvs, NVS_KEY_CURRENT_PROFILE, profile_id);
    nvs_commit(nvs);
    nvs_close(nvs);
    
    return (written == sizeof(file_data)) ? ESP_OK : ESP_FAIL;
}
```

### Загрузка профиля

```c
esp_err_t profile_load(uint8_t profile_id, profile_t* profile) {
    char path[32];
    snprintf(path, sizeof(path), "/storage/profiles/profile_%d.bin", profile_id);
    
    // 1. Чтение файла
    FILE* f = fopen(path, "rb");
    if (!f) return ESP_ERR_NOT_FOUND;
    
    profile_file_t file_data;
    size_t read = fread(&file_data, 1, sizeof(file_data), f);
    fclose(f);
    
    if (read != sizeof(file_data)) {
        return ESP_ERR_INVALID_SIZE;
    }
    
    // 2. Проверка magic и версии
    if (file_data.header.magic != 0x50524F46) {
        return ESP_ERR_INVALID_CRC;
    }
    
    // 3. Проверка CRC32
    uint32_t stored_crc = file_data.header.crc32;
    file_data.header.crc32 = 0;
    uint32_t calculated_crc = crc32_calculate(&file_data, 
        sizeof(file_data) - sizeof(uint32_t));
    
    if (stored_crc != calculated_crc) {
        LOG_ERROR("STORAGE", "Profile %d CRC mismatch", profile_id);
        return ESP_ERR_INVALID_CRC;
    }
    
    // 4. Копирование данных
    profile->profile_id = file_data.header.profile_id;
    strncpy(profile->name, file_data.header.name, 31);
    memcpy(profile->buttons, file_data.buttons, sizeof(profile->buttons));
    
    return ESP_OK;
}
```

### Сохранение изображения

```c
esp_err_t image_save(uint8_t profile_id, uint8_t button_id, 
                     const uint8_t* data, size_t size) {
    char path[48];
    snprintf(path, sizeof(path), "/storage/images/p%d_b%d.jpg", 
             profile_id, button_id);
    
    // 1. Запись файла
    FILE* f = fopen(path, "wb");
    if (!f) return ESP_FAIL;
    
    size_t written = fwrite(data, 1, size, f);
    fclose(f);
    
    // 2. Инвалидация кэша
    image_cache_invalidate(profile_id, button_id);
    
    return (written == size) ? ESP_OK : ESP_FAIL;
}
```

### Загрузка и декодирование изображения

```c
esp_err_t image_load_decoded(uint8_t profile_id, uint8_t button_id,
                             uint8_t** out_data, size_t* out_size) {
    // 1. Проверка кэша
    image_cache_entry_t* cached = image_cache_get(profile_id, button_id);
    if (cached && cached->valid) {
        *out_data = cached->data;
        *out_size = cached->size;
        return ESP_OK;
    }
    
    // 2. Загрузка из файла
    char path[48];
    snprintf(path, sizeof(path), "/storage/images/p%d_b%d.jpg", 
             profile_id, button_id);
    
    FILE* f = fopen(path, "rb");
    if (!f) return ESP_ERR_NOT_FOUND;
    
    // Определение размера
    fseek(f, 0, SEEK_END);
    size_t jpeg_size = ftell(f);
    fseek(f, 0, SEEK_SET);
    
    // Чтение JPEG
    uint8_t* jpeg_data = malloc(jpeg_size);
    fread(jpeg_data, 1, jpeg_size, f);
    fclose(f);
    
    // 3. Декодирование JPEG в RGB565
    uint8_t* rgb565_data = heap_caps_malloc(160 * 160 * 2, MALLOC_CAP_SPIRAM);
    esp_err_t ret = jpeg_decode(jpeg_data, jpeg_size, 
                                 rgb565_data, 160, 160);
    free(jpeg_data);
    
    if (ret != ESP_OK) {
        heap_caps_free(rgb565_data);
        return ret;
    }
    
    // 4. Добавление в кэш
    image_cache_add(profile_id, button_id, rgb565_data, 160 * 160 * 2);
    
    *out_data = rgb565_data;
    *out_size = 160 * 160 * 2;
    
    return ESP_OK;
}
```

## Управление свободным местом

### Проверка доступного места

```c
esp_err_t storage_get_free_space(size_t* free_bytes) {
    size_t total = 0, used = 0;
    esp_err_t ret = esp_spiffs_info("storage", &total, &used);
    if (ret == ESP_OK) {
        *free_bytes = total - used;
    }
    return ret;
}
```

### Очистка старых данных

```c
esp_err_t storage_cleanup(void) {
    // 1. Удаление кэша
    DIR* dir = opendir("/storage/cache");
    if (dir) {
        struct dirent* entry;
        while ((entry = readdir(dir)) != NULL) {
            char path[64];
            snprintf(path, sizeof(path), "/storage/cache/%s", entry->d_name);
            unlink(path);
        }
        closedir(dir);
    }
    
    // 2. Удаление неиспользуемых изображений
    // (если профиль удален, но изображения остались)
    
    return ESP_OK;
}
```

## Wear Leveling

SPIFFS автоматически реализует wear leveling для равномерного износа flash памяти.

### Рекомендации

1. **Минимизировать частые записи**: Кэшировать изменения в RAM, записывать пакетами
2. **Использовать NVS для частых обновлений**: NVS оптимизирован для частых записей
3. **Периодическая дефрагментация**: При низком свободном месте

## Резервное копирование и восстановление

### Factory Reset

```c
esp_err_t factory_reset(void) {
    // 1. Очистка NVS
    nvs_flash_erase();
    nvs_flash_init();
    
    // 2. Форматирование SPIFFS
    esp_vfs_spiffs_unregister("storage");
    esp_partition_t* partition = esp_partition_find_first(
        ESP_PARTITION_TYPE_DATA, ESP_PARTITION_SUBTYPE_DATA_SPIFFS, "storage");
    esp_partition_erase_range(partition, 0, partition->size);
    
    // 3. Повторная инициализация
    storage_init();
    
    // 4. Создание профилей по умолчанию
    create_default_profiles();
    
    return ESP_OK;
}
```

### Экспорт профиля

Профиль можно экспортировать через USB для резервного копирования:

```c
esp_err_t profile_export(uint8_t profile_id, uint8_t* buffer, size_t* size) {
    // Чтение профиля и всех связанных изображений
    // Упаковка в единый бинарный формат
    // Возврат через USB HID Raw
}
```

## Миграция данных

При обновлении формата данных:

```c
esp_err_t storage_migrate(uint16_t from_version, uint16_t to_version) {
    if (from_version == 1 && to_version == 2) {
        // Миграция формата профилей v1 -> v2
        for (int i = 0; i < 5; i++) {
            profile_t profile;
            if (profile_load_v1(i, &profile) == ESP_OK) {
                profile_save_v2(i, &profile);
            }
        }
    }
    return ESP_OK;
}
```

## Диагностика и отладка

### Проверка целостности

```c
esp_err_t storage_check_integrity(void) {
    bool errors = false;
    
    // Проверка всех профилей
    for (int i = 0; i < 5; i++) {
        profile_t profile;
        esp_err_t ret = profile_load(i, &profile);
        if (ret != ESP_OK) {
            LOG_ERROR("STORAGE", "Profile %d corrupted", i);
            errors = true;
        }
    }
    
    // Проверка изображений
    for (int p = 0; p < 5; p++) {
        for (int b = 0; b < 10; b++) {
            char path[48];
            snprintf(path, sizeof(path), "/storage/images/p%d_b%d.jpg", p, b);
            
            struct stat st;
            if (stat(path, &st) != 0) {
                LOG_WARN("STORAGE", "Image p%d_b%d missing", p, b);
            }
        }
    }
    
    return errors ? ESP_FAIL : ESP_OK;
}
```

### Статистика использования

```c
typedef struct {
    size_t total_space;
    size_t used_space;
    size_t free_space;
    uint8_t profiles_count;
    uint8_t images_count;
    size_t cache_size;
} storage_stats_t;

esp_err_t storage_get_stats(storage_stats_t* stats) {
    // Сбор статистики по использованию памяти
    esp_spiffs_info("storage", &stats->total_space, &stats->used_space);
    stats->free_space = stats->total_space - stats->used_space;
    
    // Подсчет профилей и изображений
    // ...
    
    return ESP_OK;
}
```

## Оптимизация производительности

### Предзагрузка

При переключении профиля предзагружать изображения в фоне:

```c
void profile_preload_images(uint8_t profile_id) {
    for (int i = 0; i < 10; i++) {
        uint8_t* data;
        size_t size;
        image_load_decoded(profile_id, i, &data, &size);
    }
}
```

### Асинхронная запись

Использовать очередь для асинхронной записи в flash:

```c
typedef struct {
    enum { WRITE_PROFILE, WRITE_IMAGE } type;
    uint8_t profile_id;
    uint8_t button_id;
    void* data;
    size_t size;
} storage_write_task_t;

QueueHandle_t storage_write_queue;

void storage_write_task(void* arg) {
    storage_write_task_t task;
    while (1) {
        if (xQueueReceive(storage_write_queue, &task, portMAX_DELAY)) {
            // Выполнение записи
            // Освобождение памяти
        }
    }
}
```

## Безопасность данных

### Шифрование (опционально)

Для защиты конфиденциальных данных можно использовать flash encryption:

```c
// В sdkconfig
CONFIG_SECURE_FLASH_ENC_ENABLED=y
CONFIG_SECURE_FLASH_ENCRYPTION_MODE_DEVELOPMENT=y
```

### Контрольные суммы

Все критические данные защищены CRC32 для обнаружения повреждений.

### Резервирование

Критические данные (текущий профиль, WiFi credentials) дублируются в NVS.
