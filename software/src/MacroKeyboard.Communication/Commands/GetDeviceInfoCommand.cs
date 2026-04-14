using MacroKeyboard.Communication.Protocol;
using MacroKeyboard.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text;

namespace MacroKeyboard.Communication.Commands;

/// <summary>
/// Команда GET_DEVICE_INFO для получения информации об устройстве
/// </summary>
public class GetDeviceInfoCommand
{
    private readonly ProtocolHandler _protocol;
    private readonly ILogger<GetDeviceInfoCommand> _logger;
    
    public GetDeviceInfoCommand(ProtocolHandler protocol, ILogger<GetDeviceInfoCommand> logger)
    {
        _protocol = protocol;
        _logger = logger;
    }
    
    /// <summary>
    /// Выполнить команду GET_DEVICE_INFO
    /// </summary>
    public async Task<DeviceInfo?> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var response = await _protocol.SendCommandAsync(
            ProtocolConstants.CMD_GET_DEVICE_INFO,
            Array.Empty<byte>(),
            cancellationToken: cancellationToken);
        
        if (response == null)
            return null;
        
        return ParseResponse(response);
    }
    
    private DeviceInfo? ParseResponse(ProtocolPacket packet)
    {
        try
        {
            var payload = packet.Payload;
            
            // Байт 0-15: Device ID (UUID)
            var deviceIdBytes = new byte[16];
            Array.Copy(payload, 0, deviceIdBytes, 0, 16);
            var deviceId = new Guid(deviceIdBytes).ToString();
            
            // Байт 16-19: Firmware version
            var major = payload[16];
            var minor = payload[17];
            var patch = payload[18];
            var build = payload[19];
            
            // Байт 20: Number of buttons
            var buttonCount = payload[20];
            
            // Байт 21: Number of profiles
            var profileCount = payload[21];
            
            // Байт 22: Current profile
            var currentProfile = payload[22];
            
            // Байт 23-26: Free flash space (little-endian)
            var freeSpace = BitConverter.ToUInt32(payload, 23);
            
            return new DeviceInfo
            {
                DeviceId = deviceId,
                FirmwareVersion = new Version(major, minor, patch, build),
                ButtonCount = buttonCount,
                ProfileCount = profileCount,
                CurrentProfile = currentProfile,
                FreeSpace = freeSpace,
                IsConnected = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing GET_DEVICE_INFO response");
            return null;
        }
    }
}
