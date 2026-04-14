# Настройка окружения для разработки управляющего софта

## Установка .NET 8.0 SDK

### Для Ubuntu/Debian

```bash
# Добавить Microsoft package repository
wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

# Обновить список пакетов
sudo apt-get update

# Установить .NET 8.0 SDK
sudo apt-get install -y dotnet-sdk-8.0

# Проверить установку
dotnet --version
```

### Для других дистрибутивов Linux

См. официальную документацию: https://learn.microsoft.com/en-us/dotnet/core/install/linux

### Для Windows

Скачать установщик: https://dotnet.microsoft.com/download/dotnet/8.0

### Для macOS

```bash
# Через Homebrew
brew install dotnet@8

# Или скачать установщик
# https://dotnet.microsoft.com/download/dotnet/8.0
```

## Проверка установки

После установки проверьте:

```bash
# Версия SDK
dotnet --version
# Должно показать: 8.0.x

# Список установленных SDK
dotnet --list-sdks
# Должно показать: 8.0.xxx [путь]

# Список установленных runtime
dotnet --list-runtimes
```

## Установка расширений VS Code

### Обязательные расширения

```bash
# C# Dev Kit (включает C# и .NET Runtime)
code --install-extension ms-dotnettools.csdevkit

# C# (базовая поддержка)
code --install-extension ms-dotnettools.csharp

# .NET Runtime Install Tool
code --install-extension ms-dotnettools.vscode-dotnet-runtime
```

### Рекомендуемые расширения

```bash
# NuGet Package Manager
code --install-extension jmrog.vscode-nuget-package-manager

# XAML (если используем WPF)
code --install-extension TimLariviere.vscode-xaml

# Avalonia (если используем Avalonia UI)
code --install-extension AvaloniaTeam.vscode-avalonia

# EditorConfig
code --install-extension EditorConfig.EditorConfig

# GitLens
code --install-extension eamodio.gitlens
```

## Выбор UI Framework

### Вариант 1: Avalonia UI (рекомендуется для VS Code)

```bash
# Установить шаблоны Avalonia
dotnet new install Avalonia.Templates

# Проверить установку
dotnet new list | grep avalonia
```

**Преимущества**:
- Отличная поддержка в VS Code
- Кроссплатформенность (Windows, macOS, Linux)
- Современный и активно развивается
- Хорошая документация

### Вариант 2: WPF (только Windows)

WPF уже включен в .NET SDK для Windows.

**Преимущества**:
- Зрелая технология
- Много примеров и библиотек
- Визуальный дизайнер в Visual Studio

**Недостатки**:
- Только Windows
- Нет визуального дизайнера в VS Code

## Создание проекта

### Avalonia UI проект

```bash
cd software/src

# Создать решение
dotnet new sln -n MacroKeyboard

# Создать проекты
dotnet new avalonia.mvvm -o MacroKeyboard.UI
dotnet new classlib -o MacroKeyboard.Core
dotnet new classlib -o MacroKeyboard.Communication
dotnet new classlib -o MacroKeyboard.Infrastructure

# Добавить проекты в решение
dotnet sln add MacroKeyboard.UI/MacroKeyboard.UI.csproj
dotnet sln add MacroKeyboard.Core/MacroKeyboard.Core.csproj
dotnet sln add MacroKeyboard.Communication/MacroKeyboard.Communication.csproj
dotnet sln add MacroKeyboard.Infrastructure/MacroKeyboard.Infrastructure.csproj

# Добавить ссылки между проектами
cd MacroKeyboard.UI
dotnet add reference ../MacroKeyboard.Core/MacroKeyboard.Core.csproj
dotnet add reference ../MacroKeyboard.Infrastructure/MacroKeyboard.Infrastructure.csproj
dotnet add reference ../MacroKeyboard.Communication/MacroKeyboard.Communication.csproj

cd ../MacroKeyboard.Infrastructure
dotnet add reference ../MacroKeyboard.Core/MacroKeyboard.Core.csproj

cd ../MacroKeyboard.Communication
dotnet add reference ../MacroKeyboard.Core/MacroKeyboard.Core.csproj
```

### WPF проект

```bash
cd software/src

# Создать решение
dotnet new sln -n MacroKeyboard

# Создать проекты
dotnet new wpf -o MacroKeyboard.UI
dotnet new classlib -o MacroKeyboard.Core
dotnet new classlib -o MacroKeyboard.Communication
dotnet new classlib -o MacroKeyboard.Infrastructure

# Добавить проекты в решение и ссылки (аналогично Avalonia)
```

## Установка NuGet пакетов

```bash
cd MacroKeyboard.UI

# Основные пакеты
dotnet add package CommunityToolkit.Mvvm
dotnet add package Serilog
dotnet add package Serilog.Sinks.File
dotnet add package Microsoft.Extensions.DependencyInjection

cd ../MacroKeyboard.Communication
dotnet add package HidLibrary

cd ../MacroKeyboard.Infrastructure
dotnet add package Newtonsoft.Json
dotnet add package SixLabors.ImageSharp
```

## Сборка и запуск проектов

### Быстрый старт - Сборка всех проектов

```bash
# Перейти в директорию с решением
cd /home/andrewp/elgato/software/src

# Восстановить зависимости
dotnet restore

# Собрать все проекты
dotnet build

# Собрать в Release режиме (оптимизированная версия)
dotnet build -c Release
```

### Запуск Backend (MacroKeyboard.Backend)

Backend - это фоновый сервис, который управляет устройством и обрабатывает действия.

```bash
# Перейти в директорию Backend
cd /home/andrewp/elgato/software/src/MacroKeyboard.Backend

# Запустить в Debug режиме
dotnet run

# Или запустить в Release режиме
dotnet run -c Release

# Backend будет работать в фоне и слушать IPC соединения
```

**Что делает Backend:**
- Подключается к HID устройству (макроклавиатуре)
- Обрабатывает нажатия кнопок
- Выполняет действия (запуск программ, эмуляция клавиш, и т.д.)
- Предоставляет IPC интерфейс для UI и TrayApp

### Запуск Tray App (MacroKeyboard.TrayApp)

TrayApp - это приложение в системном трее для быстрого доступа.

```bash
# Перейти в директорию TrayApp
cd /home/andrewp/elgato/software/src/MacroKeyboard.TrayApp

# Запустить
dotnet run

# Или в Release режиме
dotnet run -c Release

# Иконка появится в системном трее
```

**Что делает TrayApp:**
- Показывает иконку в системном трее
- Позволяет быстро переключать профили
- Показывает статус подключения устройства
- Открывает главное UI приложение

### Запуск UI (MacroKeyboard.UI)

UI - это главное приложение для настройки профилей и кнопок.

```bash
# Перейти в директорию UI
cd /home/andrewp/elgato/software/src/MacroKeyboard.UI

# Запустить
dotnet run

# Или в Release режиме
dotnet run -c Release
```

**Что делает UI:**
- Управление профилями
- Настройка действий для кнопок
- Загрузка изображений на дисплеи
- Мониторинг состояния устройства

### Запуск всех компонентов одновременно

Для полноценной работы системы нужно запустить все три компонента:

```bash
# Терминал 1: Backend
cd /home/andrewp/elgato/software/src/MacroKeyboard.Backend
dotnet run

# Терминал 2: TrayApp
cd /home/andrewp/elgato/software/src/MacroKeyboard.TrayApp
dotnet run

# Терминал 3: UI (опционально, только для настройки)
cd /home/andrewp/elgato/software/src/MacroKeyboard.UI
dotnet run
```

### Создание исполняемых файлов

Для создания standalone приложений:

```bash
cd /home/andrewp/elgato/software/src

# Опубликовать Backend
dotnet publish MacroKeyboard.Backend/MacroKeyboard.Backend.csproj \
    -c Release \
    -o ../publish/backend \
    --self-contained false

# Опубликовать TrayApp
dotnet publish MacroKeyboard.TrayApp/MacroKeyboard.TrayApp.csproj \
    -c Release \
    -o ../publish/trayapp \
    --self-contained false

# Опубликовать UI
dotnet publish MacroKeyboard.UI/MacroKeyboard.UI.csproj \
    -c Release \
    -o ../publish/ui \
    --self-contained false
```

Исполняемые файлы будут в директории `software/publish/`.

### Создание self-contained приложений (со встроенным .NET)

Если нужно распространять приложение без требования установки .NET:

```bash
# Для Linux
dotnet publish MacroKeyboard.Backend/MacroKeyboard.Backend.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -o ../publish/backend-linux

# Для Windows
dotnet publish MacroKeyboard.Backend/MacroKeyboard.Backend.csproj \
    -c Release \
    -r win-x64 \
    --self-contained true \
    -o ../publish/backend-windows

# Для macOS
dotnet publish MacroKeyboard.Backend/MacroKeyboard.Backend.csproj \
    -c Release \
    -r osx-x64 \
    --self-contained true \
    -o ../publish/backend-macos
```

### Проверка работоспособности

```bash
# Собрать решение
dotnet build

# Запустить приложение
cd MacroKeyboard.UI
dotnet run
```

## Настройка VS Code

Создайте `.vscode/launch.json`:

```json
{
    "version": "0.2.0",
    "configurations": [
        {
            "name": ".NET Core Launch (console)",
            "type": "coreclr",
            "request": "launch",
            "preLaunchTask": "build",
            "program": "${workspaceFolder}/software/src/MacroKeyboard.UI/bin/Debug/net8.0/MacroKeyboard.UI.dll",
            "args": [],
            "cwd": "${workspaceFolder}/software/src/MacroKeyboard.UI",
            "console": "internalConsole",
            "stopAtEntry": false
        }
    ]
}
```

Создайте `.vscode/tasks.json`:

```json
{
    "version": "2.0.0",
    "tasks": [
        {
            "label": "build",
            "command": "dotnet",
            "type": "process",
            "args": [
                "build",
                "${workspaceFolder}/software/src/MacroKeyboard.sln",
                "/property:GenerateFullPaths=true",
                "/consoleloggerparameters:NoSummary"
            ],
            "problemMatcher": "$msCompile"
        }
    ]
}
```

## Troubleshooting

### .NET SDK не найден

```bash
# Проверить PATH
echo $PATH | grep dotnet

# Добавить в PATH (если нужно)
export PATH="$PATH:/usr/share/dotnet"

# Добавить в ~/.bashrc для постоянного эффекта
echo 'export PATH="$PATH:/usr/share/dotnet"' >> ~/.bashrc
source ~/.bashrc
```

### Ошибки при установке пакетов

```bash
# Очистить кэш NuGet
dotnet nuget locals all --clear

# Восстановить пакеты
dotnet restore
```

### Проблемы с правами доступа

```bash
# Если нужны права sudo для установки
sudo apt-get install -y dotnet-sdk-8.0

# Если проблемы с доступом к NuGet
chmod -R 755 ~/.nuget
```

## Быстрая справка по командам

### Основные команды

```bash
# Перейти в директорию проекта
cd /home/andrewp/elgato/software/src

# Восстановить зависимости
dotnet restore

# Собрать все проекты
dotnet build

# Собрать в Release
dotnet build -c Release

# Очистить сборку
dotnet clean
```

### Запуск компонентов

```bash
# Backend (фоновый сервис)
cd /home/andrewp/elgato/software/src/MacroKeyboard.Backend
dotnet run

# TrayApp (системный трей)
cd /home/andrewp/elgato/software/src/MacroKeyboard.TrayApp
dotnet run

# UI (главное приложение)
cd /home/andrewp/elgato/software/src/MacroKeyboard.UI
dotnet run
```

### Публикация приложений

```bash
cd /home/andrewp/elgato/software/src

# Backend
dotnet publish MacroKeyboard.Backend/MacroKeyboard.Backend.csproj -c Release -o ../publish/backend

# TrayApp
dotnet publish MacroKeyboard.TrayApp/MacroKeyboard.TrayApp.csproj -c Release -o ../publish/trayapp

# UI
dotnet publish MacroKeyboard.UI/MacroKeyboard.UI.csproj -c Release -o ../publish/ui
```

### Однострочная команда для запуска всех компонентов

```bash
# В разных терминалах или используйте tmux/screen
cd /home/andrewp/elgato/software/src && dotnet run --project MacroKeyboard.Backend/MacroKeyboard.Backend.csproj &
cd /home/andrewp/elgato/software/src && dotnet run --project MacroKeyboard.TrayApp/MacroKeyboard.TrayApp.csproj &
cd /home/andrewp/elgato/software/src && dotnet run --project MacroKeyboard.UI/MacroKeyboard.UI.csproj
```

## Следующие шаги

После установки окружения:

1. Изучите документацию:
   - [`software/REQUIREMENTS.md`](REQUIREMENTS.md) - требования
   - [`software/plans/architecture.md`](plans/architecture.md) - архитектура
   - [`software/plans/plugin_system.md`](plans/plugin_system.md) - система плагинов

2. Создайте структуру проекта (см. выше)

3. Начните с Core модуля:
   - Модели данных (Profile, ButtonConfig, etc.)
   - Интерфейсы сервисов

4. Затем Communication модуль:
   - HID Device Manager
   - Protocol Handler

5. Потом Infrastructure:
   - Реализация сервисов
   - Repositories

6. И наконец UI:
   - Views и ViewModels
   - Стили и ресурсы

## Полезные ссылки

- [.NET 8.0 Documentation](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8)
- [Avalonia UI Documentation](https://docs.avaloniaui.net/)
- [WPF Documentation](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)
- [MVVM Toolkit](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/)
- [HidLibrary](https://github.com/mikeobrien/HidLibrary)
