using MacroKeyboard.Communication.Protocol;
using MacroKeyboard.Core.Models;
using Microsoft.Extensions.Logging;

namespace MacroKeyboard.Communication.Commands;

/// <summary>
/// Команда SET_LED_COLOR для установки цвета RGB LED
/// </summary>
public class SetLedColorCommand
{
    private readonly ProtocolHandler _protocol;
    private readonly ILogger<SetLedColorCommand> _logger;
    
    public SetLedColorCommand(ProtocolHandler protocol, ILogger<SetLedColorCommand> logger)
    {
        _protocol = protocol;
        _logger = logger;
    }
    
    /// <summary>
    /// Выполнить команду SET_LED_COLOR
    /// </summary>
    public async Task<bool> ExecuteAsync(
        byte profileId, 
        byte buttonId, 
        LedConfig led,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new byte[7];
            payload[0] = profileId;
            payload[1] = buttonId;
            payload[2] = led.R;
            payload[3] = led.G;
            payload[4] = led.B;
            payload[5] = led.Brightness;
            payload[6] = (byte)led.Effect;
            
            var response = await _protocol.SendCommandAsync(
                ProtocolConstants.CMD_SET_LED_COLOR,
                payload,
                cancellationToken: cancellationToken);
            
            if (response == null)
                return false;
            
            var status = response.Payload[0];
            
            if (status == ProtocolConstants.STATUS_OK)
            {
                _logger.LogInformation("LED color set for button {ButtonId}: RGB({R},{G},{B})", 
                    buttonId, led.R, led.G, led.B);
                return true;
            }
            
            _logger.LogError("Failed to set LED color. Status: 0x{Status:X2}", status);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting LED color");
            return false;
        }
    }
}