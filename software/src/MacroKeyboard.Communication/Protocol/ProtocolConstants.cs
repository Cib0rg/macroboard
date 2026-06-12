namespace MacroKeyboard.Communication.Protocol;

/// <summary>
/// Константы протокола обмена данными (совместимо с прошивкой)
/// </summary>
public static class ProtocolConstants
{
    // Размеры пакетов
    public const int PacketSize = 64;
    public const int PayloadSize = 56;
    
    // Маркеры пакета
    public const byte MagicByte = 0xA5;
    public const byte EndByte = 0x5A;
    
    // USB VID/PID
    public const int VendorId = 0x1209;  // pid.codes (Open Source VID)
    public const int ProductId = 0x0001; // MacroKeyboard
    
    // Device identification (must match firmware config.h USB_MANUFACTURER / USB_PRODUCT)
    public const string ManufacturerName = "Elgato";
    public const string ProductName = "Stream Deck";
    
    // Команды от PC к устройству
    public const byte CMD_PING = 0x01;
    public const byte CMD_GET_DEVICE_INFO = 0x02;
    public const byte CMD_SET_PROFILE = 0x10;
    public const byte CMD_GET_PROFILE_INFO = 0x11;
    public const byte CMD_START_IMAGE_TRANSFER = 0x20;
    public const byte CMD_IMAGE_DATA_CHUNK = 0x21;
    public const byte CMD_END_IMAGE_TRANSFER = 0x22;
    public const byte CMD_GET_BUTTON_IMAGE = 0x23;
    public const byte CMD_SET_BUTTON_ACTION         = 0x30;
    public const byte CMD_GET_BUTTON_ACTION         = 0x31;
    public const byte CMD_SET_BUTTON_NAME           = 0x32;
    public const byte CMD_SET_FOLDER_BUTTON_ACTION  = 0x33;
    public const byte CMD_SET_FOLDER_BUTTON_NAME    = 0x34;
    public const byte CMD_SET_ENCODER_ACTION        = 0x35;
    public const byte CMD_SET_BUTTON_LONG_PRESS_ACTION = 0x36;
    public const byte CMD_SET_BUTTON_LONG_PRESS_NAME   = 0x37;
    public const byte CMD_SET_LED_COLOR             = 0x40;
    public const byte CMD_SET_BACKLIGHT             = 0x41;
    public const byte CMD_GET_LED_COLOR             = 0x42;
    public const byte CMD_SET_FOLDER_BUTTON_LED     = 0x43;
    public const byte CMD_SAVE_PROFILE = 0x50;
    public const byte CMD_LOAD_PROFILE = 0x51;
    public const byte CMD_DELETE_PROFILE = 0x52;
    public const byte CMD_REFRESH_DISPLAYS = 0x53;
    public const byte CMD_START_OTA_UPDATE = 0x60;
    public const byte CMD_GET_OTA_STATUS = 0x61;
    public const byte CMD_SET_WIFI_CREDENTIALS = 0x70;
    public const byte CMD_GET_WIFI_STATUS = 0x71;
    public const byte CMD_ENABLE_DEBUG_LOG = 0x80;
    public const byte CMD_FACTORY_RESET = 0x81;
    
    // События от устройства к PC
    public const byte EVENT_BUTTON_PRESSED = 0xF0;
    public const byte EVENT_ENCODER_ROTATED = 0xF1;
    public const byte EVENT_ENCODER_BUTTON = 0xF2;
    public const byte EVENT_PROFILE_CHANGED = 0xF3;
    public const byte EVENT_DEVICE_READY = 0xF4;
    public const byte EVENT_FOLDER_ENTERED = 0xF5;
    public const byte EVENT_FOLDER_EXITED = 0xF6;
    public const byte EVENT_ERROR = 0xFF;
    
    // Коды статуса
    public const byte STATUS_OK = 0x00;
    public const byte STATUS_ERROR = 0xFF;
    public const byte STATUS_RETRY = 0x01;
    
    // Тайм-ауты
    public const int DefaultTimeout = 3000; // мс (increased from 1000 to handle FreeRTOS queue latency)
    public const int ImageTransferTimeout = 30000; // мс
    
    // Размер фрагмента изображения
    public const int ImageChunkSize = 50;
}
