# Docker Integration - Диаграммы

## Архитектура Docker решения

### Общая схема

```mermaid
graph TB
    subgraph "Хост система"
        DEV[👨‍💻 Разработчик]
        CODE[📁 Исходный код<br/>firmware/macro-keyboard]
        MAKE[📋 Makefile]
        COMPOSE[🐳 docker-compose.yml]
        USB[🔌 USB устройство<br/>/dev/ttyUSB0]
    end
    
    subgraph "Docker контейнер"
        IMAGE[📦 espressif/idf:v5.3]
        IDF[🛠️ ESP-IDF v5.3]
        TOOLS[⚙️ Инструменты<br/>gcc, cmake, ninja]
        BUILD[🔨 Процесс сборки<br/>idf.py build]
    end
    
    subgraph "Docker volumes"
        CCACHE[💾 ccache<br/>Кэш компиляции]
        CONFIG[⚙️ .espressif<br/>Конфигурация]
    end
    
    subgraph "Результат"
        BIN[📦 firmware.bin]
        DEVICE[🎮 ESP32-S3<br/>Устройство]
    end
    
    DEV -->|make build| MAKE
    MAKE -->|docker-compose run| COMPOSE
    COMPOSE -->|запускает| IMAGE
    IMAGE -->|содержит| IDF
    IMAGE -->|содержит| TOOLS
    CODE -->|монтируется в| BUILD
    IDF -->|использует| BUILD
    TOOLS -->|использует| BUILD
    BUILD -->|использует| CCACHE
    BUILD -->|использует| CONFIG
    BUILD -->|создает| BIN
    
    DEV -->|make flash| MAKE
    MAKE -->|docker-compose run| COMPOSE
    USB -->|пробрасывается в| IMAGE
    BIN -->|прошивается через| USB
    USB -->|подключен к| DEVICE
    
    style DEV fill:#e1f5ff
    style IMAGE fill:#fff3e0
    style BUILD fill:#f3e5f5
    style BIN fill:#e8f5e9
    style DEVICE fill:#fce4ec
```

## Workflow разработки

### Цикл разработки с Docker

```mermaid
sequenceDiagram
    participant Dev as 👨‍💻 Разработчик
    participant Make as 📋 Makefile
    participant Docker as 🐳 Docker
    participant Container as 📦 Контейнер
    participant Cache as 💾 ccache
    participant Device as 🎮 ESP32-S3
    
    Dev->>Make: make build
    Make->>Docker: docker-compose run esp-idf
    Docker->>Container: Запуск контейнера
    Container->>Cache: Проверка кэша
    
    alt Файлы в кэше
        Cache-->>Container: Использовать кэш
        Note over Container: Сборка 30-60 сек
    else Нет в кэше
        Note over Container: Сборка 5-10 мин
        Container->>Cache: Сохранить в кэш
    end
    
    Container-->>Make: firmware.bin готов
    Make-->>Dev: ✅ Сборка завершена
    
    Dev->>Make: make flash
    Make->>Docker: docker-compose run esp-idf
    Docker->>Container: Запуск с USB
    Container->>Device: Прошивка через USB
    Device-->>Container: ✅ Прошито
    Container-->>Make: Готово
    Make-->>Dev: ✅ Прошивка завершена
    
    Dev->>Make: make monitor
    Make->>Docker: docker-compose run esp-idf
    Docker->>Container: Запуск с USB
    Container->>Device: Подключение к serial
    Device-->>Container: Логи
    Container-->>Dev: Отображение логов
```

## Сравнение подходов

### Локальная установка vs Docker

```mermaid
graph LR
    subgraph "Локальная установка"
        L1[📥 Установка зависимостей<br/>30-60 минут]
        L2[📦 Клонирование ESP-IDF<br/>~2 GB]
        L3[🔧 Установка инструментов<br/>./install.sh]
        L4[⚙️ Активация окружения<br/>. export.sh каждый раз]
        L5[🔨 Сборка проекта<br/>idf.py build]
        
        L1 --> L2 --> L3 --> L4 --> L5
    end
    
    subgraph "Docker подход"
        D1[🐳 Установка Docker<br/>5 минут]
        D2[📥 Скачивание образа<br/>docker pull]
        D3[🔨 Сборка проекта<br/>make build]
        
        D1 --> D2 --> D3
    end
    
    style L1 fill:#ffebee
    style L2 fill:#ffebee
    style L3 fill:#ffebee
    style L4 fill:#ffebee
    style L5 fill:#e8f5e9
    
    style D1 fill:#e8f5e9
    style D2 fill:#e8f5e9
    style D3 fill:#e8f5e9
```

### Время настройки

```mermaid
gantt
    title Время настройки окружения
    dateFormat X
    axisFormat %M мин
    
    section Локальная установка
    Установка зависимостей :a1, 0, 15
    Клонирование ESP-IDF :a2, after a1, 10
    Установка инструментов :a3, after a2, 20
    Настройка окружения :a4, after a3, 5
    Первая сборка :a5, after a4, 10
    
    section Docker
    Установка Docker :b1, 0, 5
    Скачивание образа :b2, after b1, 5
    Первая сборка :b3, after b2, 10
```

## Структура volumes

### Docker volumes для кэширования

```mermaid
graph TB
    subgraph "Хост система"
        HOST[💻 Хост]
    end
    
    subgraph "Docker volumes"
        CCACHE[💾 elgato-ccache<br/>Кэш компиляции<br/>~2 GB]
        CONFIG[⚙️ elgato-esp-config<br/>Инструменты ESP-IDF<br/>~500 MB]
    end
    
    subgraph "Контейнер 1"
        C1[📦 Сборка проекта]
        C1_CCACHE[/root/.ccache]
        C1_CONFIG[/root/.espressif]
    end
    
    subgraph "Контейнер 2"
        C2[📦 Прошивка устройства]
        C2_CCACHE[/root/.ccache]
        C2_CONFIG[/root/.espressif]
    end
    
    HOST -->|создает| CCACHE
    HOST -->|создает| CONFIG
    
    CCACHE -->|монтируется в| C1_CCACHE
    CONFIG -->|монтируется в| C1_CONFIG
    
    CCACHE -->|монтируется в| C2_CCACHE
    CONFIG -->|монтируется в| C2_CONFIG
    
    C1_CCACHE -->|сохраняет| CCACHE
    C2_CCACHE -->|использует| CCACHE
    
    style CCACHE fill:#e3f2fd
    style CONFIG fill:#f3e5f5
    style C1 fill:#fff3e0
    style C2 fill:#fff3e0
```

## Производительность сборки

### Влияние ccache на время сборки

```mermaid
graph LR
    subgraph "Первая сборка"
        F1[🔨 Компиляция всех файлов]
        F2[💾 Сохранение в ccache]
        F3[⏱️ 5-10 минут]
        
        F1 --> F2 --> F3
    end
    
    subgraph "Вторая сборка без изменений"
        S1[💾 Все из кэша]
        S2[⏱️ 10-20 секунд]
        
        S1 --> S2
    end
    
    subgraph "Сборка с изменениями"
        T1[💾 Большинство из кэша]
        T2[🔨 Компиляция измененных]
        T3[⏱️ 30-60 секунд]
        
        T1 --> T2 --> T3
    end
    
    style F3 fill:#ffebee
    style S2 fill:#e8f5e9
    style T3 fill:#fff9c4
```

## CI/CD интеграция

### GitHub Actions workflow

```mermaid
graph TB
    subgraph "GitHub"
        PUSH[📤 git push]
        REPO[📦 Repository]
    end
    
    subgraph "GitHub Actions"
        TRIGGER[🎯 Trigger workflow]
        CHECKOUT[📥 Checkout code]
        DOCKER[🐳 Pull ESP-IDF image]
        BUILD[🔨 Build firmware]
        TEST[✅ Run tests]
        ARTIFACT[📦 Upload artifacts]
    end
    
    subgraph "Результат"
        BIN[📦 firmware.bin]
        RELEASE[🚀 GitHub Release]
    end
    
    PUSH --> REPO
    REPO --> TRIGGER
    TRIGGER --> CHECKOUT
    CHECKOUT --> DOCKER
    DOCKER --> BUILD
    BUILD --> TEST
    TEST --> ARTIFACT
    ARTIFACT --> BIN
    BIN --> RELEASE
    
    style PUSH fill:#e3f2fd
    style BUILD fill:#fff3e0
    style TEST fill:#e8f5e9
    style RELEASE fill:#f3e5f5
```

### Пример GitHub Actions workflow

```yaml
name: Build Firmware

on: [push, pull_request]

jobs:
  build:
    runs-on: ubuntu-latest
    
    steps:
      - name: Checkout code
        uses: actions/checkout@v3
      
      - name: Build firmware
        run: |
          cd firmware
          docker-compose run --rm esp-idf idf.py build
      
      - name: Upload artifacts
        uses: actions/upload-artifact@v3
        with:
          name: firmware
          path: firmware/macro-keyboard/build/*.bin
```

## Отладка в Docker

### Подключение GDB через Docker

```mermaid
sequenceDiagram
    participant Dev as 👨‍💻 Разработчик
    participant Terminal1 as 🖥️ Терминал 1
    participant Terminal2 as 🖥️ Терминал 2
    participant OpenOCD as 🔧 OpenOCD
    participant GDB as 🐛 GDB
    participant Device as 🎮 ESP32-S3
    
    Dev->>Terminal1: docker-compose run esp-idf
    Terminal1->>OpenOCD: openocd -f board/esp32s3-builtin.cfg
    OpenOCD->>Device: Подключение через JTAG
    Device-->>OpenOCD: ✅ Подключено
    OpenOCD-->>Terminal1: Listening on port 3333
    
    Dev->>Terminal2: docker-compose run esp-idf
    Terminal2->>GDB: xtensa-esp32s3-elf-gdb
    GDB->>OpenOCD: target remote :3333
    OpenOCD-->>GDB: ✅ Подключено
    
    Dev->>GDB: break app_main
    Dev->>GDB: continue
    Device-->>GDB: Breakpoint hit
    GDB-->>Dev: Отображение состояния
```

## VS Code Dev Container

### Интеграция с VS Code

```mermaid
graph TB
    subgraph "VS Code"
        VSCODE[📝 VS Code]
        DEVCONTAINER[🐳 Dev Container Extension]
        CONFIG[⚙️ .devcontainer/devcontainer.json]
    end
    
    subgraph "Docker"
        IMAGE[📦 espressif/idf:v5.3]
        CONTAINER[🔧 Running Container]
    end
    
    subgraph "Возможности"
        INTELLISENSE[💡 IntelliSense]
        DEBUG[🐛 Debugging]
        TERMINAL[🖥️ Integrated Terminal]
        EXTENSIONS[🔌 Extensions]
    end
    
    VSCODE --> DEVCONTAINER
    DEVCONTAINER --> CONFIG
    CONFIG --> IMAGE
    IMAGE --> CONTAINER
    
    CONTAINER --> INTELLISENSE
    CONTAINER --> DEBUG
    CONTAINER --> TERMINAL
    CONTAINER --> EXTENSIONS
    
    style VSCODE fill:#007acc
    style CONTAINER fill:#fff3e0
    style INTELLISENSE fill:#e8f5e9
    style DEBUG fill:#e8f5e9
    style TERMINAL fill:#e8f5e9
    style EXTENSIONS fill:#e8f5e9
```

## Использование ресурсов

### Сравнение использования диска

```mermaid
pie title Использование диска
    "ESP-IDF исходники" : 2000
    "Компилятор и инструменты" : 1000
    "Python зависимости" : 300
    "Кэш сборки ccache" : 2000
    "Собранный проект" : 200
```

### Использование памяти при сборке

```mermaid
graph LR
    subgraph "Процесс сборки"
        START[🚀 Старт]
        PARSE[📖 Парсинг CMake<br/>~200 MB RAM]
        COMPILE[🔨 Компиляция<br/>~500 MB RAM]
        LINK[🔗 Линковка<br/>~300 MB RAM]
        DONE[✅ Готово]
        
        START --> PARSE --> COMPILE --> LINK --> DONE
    end
    
    style START fill:#e3f2fd
    style COMPILE fill:#fff3e0
    style DONE fill:#e8f5e9
```

## Рекомендуемая конфигурация

### Минимальные требования

```mermaid
graph TB
    subgraph "Системные требования"
        CPU[💻 CPU<br/>2+ ядра]
        RAM[🧠 RAM<br/>4+ GB]
        DISK[💾 Диск<br/>10+ GB свободно]
        OS[🐧 ОС<br/>Linux/macOS/Windows]
    end
    
    subgraph "Программное обеспечение"
        DOCKER[🐳 Docker<br/>20.10+]
        COMPOSE[🐳 Docker Compose<br/>2.0+]
    end
    
    subgraph "Опционально"
        GIT[📦 Git]
        MAKE[🔧 Make]
        VSCODE[📝 VS Code]
    end
    
    CPU --> DOCKER
    RAM --> DOCKER
    DISK --> DOCKER
    OS --> DOCKER
    
    DOCKER --> COMPOSE
    
    style CPU fill:#e3f2fd
    style RAM fill:#e3f2fd
    style DISK fill:#e3f2fd
    style DOCKER fill:#fff3e0
    style COMPOSE fill:#fff3e0
```

## Troubleshooting Flow

### Решение проблем с USB

```mermaid
graph TD
    START[❌ Ошибка доступа к USB]
    CHECK1{Устройство<br/>существует?}
    CHECK2{Права<br/>доступа?}
    CHECK3{Docker<br/>privileged?}
    
    SOL1[✅ Подключить устройство]
    SOL2[✅ sudo chmod 666 /dev/ttyUSB0<br/>или<br/>sudo usermod -aG dialout USER]
    SOL3[✅ Добавить --privileged<br/>в docker-compose.yml]
    
    SUCCESS[✅ Проблема решена]
    
    START --> CHECK1
    CHECK1 -->|Нет| SOL1
    CHECK1 -->|Да| CHECK2
    CHECK2 -->|Нет| SOL2
    CHECK2 -->|Да| CHECK3
    CHECK3 -->|Нет| SOL3
    
    SOL1 --> SUCCESS
    SOL2 --> SUCCESS
    SOL3 --> SUCCESS
    
    style START fill:#ffebee
    style SUCCESS fill:#e8f5e9
    style SOL1 fill:#fff9c4
    style SOL2 fill:#fff9c4
    style SOL3 fill:#fff9c4
```

## Итоговое сравнение

### Преимущества и недостатки

```mermaid
graph TB
    subgraph "Docker подход"
        D_PROS[✅ Преимущества<br/>• Быстрая настройка<br/>• Воспроизводимость<br/>• Изоляция<br/>• CI/CD готовность]
        D_CONS[⚠️ Недостатки<br/>• Немного медленнее<br/>• Сложнее отладка<br/>• Требует Docker]
    end
    
    subgraph "Локальная установка"
        L_PROS[✅ Преимущества<br/>• Максимальная скорость<br/>• Проще отладка<br/>• Прямой доступ к железу]
        L_CONS[⚠️ Недостатки<br/>• Долгая настройка<br/>• Засоряет систему<br/>• Проблемы с версиями]
    end
    
    style D_PROS fill:#e8f5e9
    style D_CONS fill:#fff9c4
    style L_PROS fill:#e8f5e9
    style L_CONS fill:#ffebee
```

---

**Версия документа**: 1.0  
**Дата**: 2026-03-25  
**Статус**: Готов к использованию
