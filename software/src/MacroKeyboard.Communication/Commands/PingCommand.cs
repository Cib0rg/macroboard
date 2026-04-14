using MacroKeyboard.Communication.Protocol;
using Microsoft.Extensions.Logging;

namespace MacroKeyboard.Communication.Commands;

/// <summary>
/// Команда PING для проверки связи с устройством
/// </summary>
public class PingCommand
{
    private readonly ProtocolHandler _protocol;
    private readonly ILogger<PingCommand> _logger;
    
    public PingCommand(ProtocolHandler protocol, ILogger<PingCommand> logger)
    {
        _protocol = protocol;
        _logger = logger;
    }
    
    /// <summary>
    /// Выполнить команду PING
    /// </summary>
    public async Task<PingResponse?> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var response = await _protocol.SendCommandAsync(
            ProtocolConstants.CMD_PING,
            Array.Empty<byte>(),
            cancellationToken: cancellationToken);
        
        if (response == null)
            return null;
        
        return ParseResponse(response);
    }
    
    private PingResponse? ParseResponse(ProtocolPacket packet)
    {
        try
        {
            var payload = packet.Payload;
            
            // Байт 0-3: Firmware version (major.minor.patch.build)
            var major = payload[0];
            var minor = payload[1];
            var patch = payload[2];
            var build = payload[3];
            
            // Байт 4-7: Uptime в секундах (little-endian)
            var uptime = BitConverter.ToUInt32(payload, 4);
            
            // Байт 8: Current profile ID
            var currentProfile = payload[8];
            
            return new PingResponse
            {
                FirmwareVersion = new Version(major, minor, patch, build),
                Uptime = uptime,
                CurrentProfile = currentProfile
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing PING response");
            return null;
        }
    }
}

/// <summary>
/// Ответ на команду PING
/// </summary>
public class PingResponse
{
    public Version FirmwareVersion { get; set; } = new Version(1, 0, 0);
    public uint Uptime { get; set; }
    public byte CurrentProfile { get; set; }
}
