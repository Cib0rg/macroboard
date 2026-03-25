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

## Проверка работоспособности

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

## Следующие шаги

После установки окружения:

1. Изучите документацию:
   - [`software/REQUIREMENTS.md`](REQUIREMENTS.md) - требования
   - [`software/plans/architecture.md`](plans/architecture.md) - архитектура
   - [`software/plans/diagrams.md`](plans/diagrams.md) - диаграммы

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
