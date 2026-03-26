# Статус сборки прошивки

## Текущее состояние

✅ **Компиляция**: Все файлы успешно компилируются (1060/1062 файлов)
❌ **Линковка**: Ошибка линковки - отсутствует callback `tud_hid_descriptor_report_cb`

## Проблема

TinyUSB HID device класс требует callback функцию `tud_hid_descriptor_report_cb`, которая определена в [`usb/usb_descriptors.c`](main/usb/usb_descriptors.c), но по какой-то причине не линкуется.

### Ошибка линковки:

```
undefined reference to `tud_hid_descriptor_report_cb'
```

## Возможные причины

1. **Файл не компилируется** - хотя он есть в CMakeLists.txt
2. **Функция удалена оптимизатором** - если не используется напрямую
3. **Неправильная сигнатура функции** - не совпадает с ожидаемой TinyUSB
4. **Проблема с weak symbols** - TinyUSB может использовать weak linking

## Решения

### Вариант 1: Использовать пример из ESP-IDF

Скопировать рабочий пример TinyUSB HID из ESP-IDF:
```bash
cp -r $IDF_PATH/examples/peripherals/usb/device/tusb_hid/main/tusb_* firmware/main/usb/
```

### Вариант 2: Упростить USB модуль

Убрать прямое использование TinyUSB API и использовать только обертку `esp_tinyusb`:

```c
// Вместо прямых вызовов tud_hid_*
// Использовать tusb_hid_keyboard_report() из esp_tinyusb
```

### Вариант 3: Добавить атрибут функции

Добавить `__attribute__((used))` к функции, чтобы предотвратить удаление:

```c
__attribute__((used))
uint8_t const* tud_hid_descriptor_report_cb(uint8_t instance) {
    // ...
}
```

### Вариант 4: Проверить компиляцию файла

Убедиться, что `usb_descriptors.c` действительно компилируется:

```bash
cd firmware
docker run --rm -v "$(pwd):/project" -w /project espressif/idf:v5.3 \
  bash -c "idf.py build -v 2>&1 | grep usb_descriptors"
```

## Рекомендуемое решение

Самое простое - использовать готовый пример из ESP-IDF и адаптировать под наши нужды. ESP-IDF содержит рабочие примеры TinyUSB HID в:

```
$IDF_PATH/examples/peripherals/usb/device/tusb_hid/
$IDF_PATH/examples/peripherals/usb/device/tusb_composite_hid_msc/
```

## Альтернатива

Если TinyUSB вызывает проблемы, можно использовать нативный USB HID ESP32-S3 через `esp_hid` компонент, который проще в использовании.

## Текущие файлы

Все файлы прошивки созданы и компилируются:
- ✅ Hardware драйверы (GC9A01, buttons, encoder, LEDs)
- ✅ Protocol handler
- ✅ Storage (NVS, SPIFFS, profiles, images)
- ✅ Profile manager
- ✅ Network (WiFi, OTA)
- ✅ Main initialization
- ⚠️ USB модули (компилируются, но не линкуются)

## Следующие шаги

1. Исправить USB descriptors callback
2. Протестировать сборку
3. Прошить на реальное устройство
4. Отладить работу с дисплеями и кнопками
