using MacroKeyboard.Communication.Protocol;
using Microsoft.Extensions.Logging;

namespace MacroKeyboard.Communication.Commands;

/// <summary>
/// Command to set display backlight brightness via PWM (0-255)
/// </summary>
public class SetDisplayBrightnessCommand
{
    private readonly ProtocolHandler _protocol;
    private readonly ILogger<SetDisplayBrightnessCommand> _logger;
    
    public SetDisplayBrightnessCommand(ProtocolHandler protocol, ILogger<SetDisplayBrightnessCommand> logger)
    {
        _protocol = protocol;
        _logger = logger;
    }
    
    /// <summary>
    /// Set display backlight brightness
    /// </summary>
    /// <param name="brightness">Brightness level (0-255, 0 = off)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Actual brightness reported by device, or null on failure</returns>
    public async Task<byte?> ExecuteAsync(byte brightness, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new byte[2];
            payload[0] = brightness > 0 ? (byte)1 : (byte)0;  // enabled flag
            payload[1] = brightness;                            // brightness level
            
            var response = await _protocol.SendCommandAsync(
                ProtocolConstants.CMD_SET_BACKLIGHT,
                payload,
                cancellationToken: cancellationToken);
            
            if (response == null)
                return null;
            
            var status = response.Payload[0];
            
            if (status == ProtocolConstants.STATUS_OK)
            {
                var actualBrightness = response.Payload.Length > 1 ? response.Payload[1] : brightness;
                _logger.LogInformation("Display brightness set to {Brightness}", actualBrightness);
                return actualBrightness;
            }
            
            _logger.LogError("Failed to set display brightness. Status: 0x{Status:X2}", status);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting display brightness");
            return null;
        }
    }
}
