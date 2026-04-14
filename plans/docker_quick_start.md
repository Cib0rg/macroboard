# Docker Quick Start - Быстрый старт

## TL;DR - Самое важное

```bash
# 1. Установить Docker (если еще нет)
sudo apt-get install docker.io docker-compose
sudo usermod -aG docker $USER
newgrp docker

# 2. Перейти в директорию firmware
cd firmware

# 3. Собрать проект
make build

# 4. Прошить устройство
make flash PORT=/dev/ttyUSB0

# 5. Открыть монитор
make monitor PORT=/dev/ttyUSB0
```

## Зачем Docker?

### Проблемы без Docker

❌ Долгая установка ESP-IDF (30-60 минут)  
❌ Множество системных зависимостей  
❌ Конфликты версий библиотек  
❌ "У меня работает, а у тебя нет"  
❌ Сложная настройка CI/CD  

### Решение с Docker

✅ Установка за 5 минут  
✅ Все зависимости в контейнере  
✅ Одинаковое окружение везде  
✅ Воспроизводимые сборки  
✅ Готово для CI/CD  

## Официальные образы Espressif

```bash
# Рекомендуемый образ для нашего проекта
espressif/idf:v5.3

# Что внутри:
# ✅ ESP-IDF v5.3
# ✅ Компилятор для ESP32-S3
# ✅ Все инструменты (cmake, ninja, ccache)
# ✅ Python с зависимостями
# ✅ OpenOCD для отладки
```

## Структура файлов (что нужно создать)

```
firmware/
├── docker-compose.yml      # ⭐ Главный файл конфигурации
├── Makefile               # ⭐ Упрощенные команды
├── .dockerignore          # Исключения для Docker
├── Dockerfile             # (опционально) Кастомизация
└── scripts/
    ├── docker-build.sh    # Скрипт сборки
    ├── docker-flash.sh    # Скрипт прошивки
    └── docker-monitor.sh  # Скрипт мониторинга
```

## Основные команды

### Через Makefile (рекомендуется)

```bash
make build              # Собрать проект
make flash              # Прошить устройство
make monitor            # Открыть serial monitor
make flash-monitor      # Прошить и открыть monitor
make menuconfig         # Настроить проект
make clean              # Очистить сборку
make size               # Показать размер прошивки
make shell              # Открыть shell в контейнере
```

### Напрямую через docker-compose

```bash
docker-compose run --rm esp-idf idf.py build
docker-compose run --rm esp-idf idf.py -p /dev/ttyUSB0 flash
docker-compose run --rm esp-idf idf.py -p /dev/ttyUSB0 monitor
docker-compose run --rm esp-idf idf.py menuconfig
```

## Производительность

### Первая сборка
- **Время**: 5-10 минут
- **Причина**: Компиляция всех компонентов ESP-IDF

### Последующие сборки
- **Время**: 30-60 секунд
- **Причина**: ccache кэширует скомпилированные файлы

### Оптимизация
```bash
# Проверить статистику ccache
docker-compose run --rm esp-idf ccache -s

# Увеличить размер кэша (если нужно)
docker-compose run --rm esp-idf ccache --max-size=5G
```

## Сравнение подходов

| Критерий | Локальная установка | Docker |
|----------|---------------------|--------|
| ⏱️ Время настройки | 30-60 минут | 5-10 минут |
| 💾 Размер | ~3-4 GB | ~2.5 GB |
| 🚀 Скорость сборки | Быстрее на 10-20% | Немного медленнее |
| 🔄 Воспроизводимость | Зависит от системы | 100% |
| 🔒 Изоляция | Нет | Да |
| 🤖 CI/CD | Сложнее | Проще |
| 🐛 Отладка | Проще | Сложнее |

## Рекомендации

### ✅ Используйте Docker если:

- Работаете в команде
- Нужна воспроизводимость сборок
- Планируете CI/CD
- Не хотите засорять систему
- Работаете на нескольких машинах

### ⚠️ Используйте локальную установку если:

- Работаете один
- Нужна максимальная производительность
- Активно используете отладку с GDB
- Часто меняете конфигурацию ESP-IDF

### 💡 Гибридный подход (лучшее решение):

- **Docker** для CI/CD и командной работы
- **Локальная установка** для активной разработки

## Troubleshooting

### Проблема: Permission denied для /dev/ttyUSB0

```bash
# Решение 1: Добавить в группу dialout
sudo usermod -aG dialout $USER
newgrp docker

# Решение 2: Дать права на устройство
sudo chmod 666 /dev/ttyUSB0

# Решение 3: udev правила (постоянное решение)
echo 'SUBSYSTEM=="usb", ATTR{idVendor}=="303a", MODE="0666"' | \
  sudo tee /etc/udev/rules.d/99-esp32.rules
sudo udevadm control --reload-rules
```

### Проблема: Контейнер не видит USB

```bash
# Проверить устройство
ls -la /dev/ttyUSB* /dev/ttyACM*

# Проверить в контейнере
docker-compose run --rm esp-idf ls -la /dev/ttyUSB*
```

### Проблема: Медленная сборка

```bash
# Проверить ccache
docker-compose run --rm esp-idf ccache -s

# Очистить и пересобрать
make fullclean
make build
```

## Следующие шаги

1. ✅ Изучить план: [`plans/docker_integration.md`](docker_integration.md)
2. ⬜ Создать конфигурационные файлы
3. ⬜ Протестировать сборку
4. ⬜ Обновить документацию

## Полезные ссылки

- 📖 [Детальный план интеграции](docker_integration.md)
- 🐳 [ESP-IDF Docker Images](https://docs.espressif.com/projects/esp-idf/en/latest/esp32s3/api-guides/tools/idf-docker-image.html)
- 🐳 [Docker Hub - espressif/idf](https://hub.docker.com/r/espressif/idf)
- 📚 [ESP-IDF Programming Guide](https://docs.espressif.com/projects/esp-idf/en/latest/esp32s3/)

---

**Версия**: 1.0  
**Дата**: 2026-03-25
