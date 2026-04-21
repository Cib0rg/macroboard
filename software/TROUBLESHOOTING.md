# Руководство по устранению неполадок

## 🔴 Ошибка: Port Already in Use (SocketException 10048)

### Симптомы
```
System.Net.Sockets.SocketException (10048): Обычно разрешается только одно использование адреса сокета (протокол/сетевой адрес/порт).
```

### Причина
Порт 28195 (IPC) или 28196 (WebSocket) уже используется другим процессом.

### Решения

#### 1. Остановить предыдущий экземпляр Backend (РЕКОМЕНДУЕТСЯ)

**Windows:**
```powershell
# Найти процесс
Get-Process | Where-Object {$_.ProcessName -like "*MacroKeyboard.Backend*"}

# Или найти по порту
netstat -ano | findstr :28195

# Остановить процесс по PID
taskkill /PID <PID> /F
```

**Linux/Mac:**
```bash
# Найти процесс по порту
lsof -i :28195
# или
netstat -tulpn | grep 28195

# Остановить процесс
kill <PID>
# или принудительно
kill -9 <PID>
```

#### 2. Изменить порт в настройках

Отредактируйте [`appsettings.json`](src/MacroKeyboard.Backend/appsettings.json):

```json
{
  "IpcPort": 28197,
  "WebSocketPort": 28198
}
```

#### 3. Подождать TIME_WAIT (1-2 минуты)

Если Backend был недавно остановлен, порт может быть в состоянии TIME_WAIT. Подождите 1-2 минуты и попробуйте снова.

---

## 🔧 Улучшения в коде (реализовано)

### Проверка занятости порта

Добавлена проверка перед запуском ([`IpcServer.cs:43`](src/MacroKeyboard.Backend/Services/IpcServer.cs)):

```csharp
if (IsPortInUse(_port))
{
    _logger.LogError("Port {Port} is already in use. Another instance of Backend may be running.", _port);
    _logger.LogError("Please stop the other instance or change the port in appsettings.json");
    throw new InvalidOperationException($"Port {_port} is already in use");
}
```

### SO_REUSEADDR опция

Добавлена опция для быстрого переиспользования порта:

```csharp
_listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
```

### Детальное логирование ошибок

При ошибке 10048 выводятся:
- Возможные причины
- Пошаговые решения
- Команды для диагностики

---

## 🐛 Другие распространенные проблемы

### HID Device Not Found

**Симптомы:**
```
Device not found (VID: 0x303A, PID: 0x4008)
```

**Решения:**
1. Проверьте подключение USB
2. Проверьте, что прошивка загружена на устройство
3. На Linux: проверьте права доступа к USB устройству
   ```bash
   sudo chmod 666 /dev/hidraw*
   ```
4. Перезагрузите устройство

### UI Not Launching from TrayApp

**Симптомы:**
```
UI executable not found
```

**Решения:**
1. Убедитесь, что MacroKeyboard.UI собран
2. Проверьте путь к executable
3. Запустите UI вручную для проверки

### Settings Not Saving

**Симптомы:**
Настройки сбрасываются после перезапуска

**Решения:**
1. Проверьте права доступа к директории AppData
2. Проверьте логи на ошибки сохранения
3. Путь к настройкам: `%AppData%/MacroKeyboard/settings.json`

---

## 📊 Диагностические команды

### Проверка портов

**Windows:**
```powershell
# Все занятые порты
netstat -ano

# Конкретный порт
netstat -ano | findstr :28195

# Процессы с сетевыми подключениями
Get-NetTCPConnection -LocalPort 28195
```

**Linux:**
```bash
# Все занятые порты
netstat -tulpn

# Конкретный порт
lsof -i :28195

# Альтернатива
ss -tulpn | grep 28195
```

### Проверка процессов

**Windows:**
```powershell
Get-Process | Where-Object {$_.ProcessName -like "*MacroKeyboard*"}
```

**Linux:**
```bash
ps aux | grep MacroKeyboard
```

### Проверка USB устройств

**Windows:**
```powershell
# Device Manager или
Get-PnpDevice | Where-Object {$_.FriendlyName -like "*HID*"}
```

**Linux:**
```bash
# Список USB устройств
lsusb

# HID устройства
ls -la /dev/hidraw*

# Детальная информация
udevadm info /dev/hidraw0
```

---

## 🔍 Логи

### Расположение логов

- **Backend:** `software/src/MacroKeyboard.Backend/logs/`
- **UI:** `%AppData%/MacroKeyboard/logs/`
- **TrayApp:** `%AppData%/MacroKeyboard/logs/`

### Уровни логирования

Измените в `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft": "Warning"
    }
  }
}
```

Уровни: `Trace` < `Debug` < `Information` < `Warning` < `Error` < `Critical`

---

## 🚑 Экстренные решения

### Полный сброс

1. Остановить все процессы MacroKeyboard
2. Удалить `%AppData%/MacroKeyboard`
3. Пересобрать проекты
4. Запустить Backend заново

### Изменить все порты

В `appsettings.json`:
```json
{
  "IpcPort": 29195,
  "WebSocketPort": 29196
}
```

### Запуск с другими правами

**Windows:** Запустить от администратора  
**Linux:** Использовать `sudo` (не рекомендуется для production)

---

## 📞 Получение помощи

### Информация для отчета об ошибке

При создании issue включите:

1. **Версия ОС:** Windows/Linux/Mac + версия
2. **Версия .NET:** `dotnet --version`
3. **Логи:** Последние 50 строк из logs/
4. **Команда запуска:** Как запускали Backend
5. **Занятые порты:** Вывод `netstat -ano | findstr :28195`
6. **Процессы:** Вывод `Get-Process | Where-Object {$_.ProcessName -like "*MacroKeyboard*"}`

### Полезные ссылки

- [GitHub Issues](https://github.com/your-repo/issues)
- [Documentation](../README.md)
- [Setup Guide](SETUP.md)

---

**Последнее обновление:** 2026-04-19  
**Версия:** 1.0
