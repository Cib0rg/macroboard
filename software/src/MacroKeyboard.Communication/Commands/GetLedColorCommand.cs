using MacroKeyboard.Communication.Protocol;
using MacroKeyboard.Core.Models;
using Microsoft.Extensions.Logging;

namespace MacroKeyboard.Communication.Commands;

/// <summary>
/// Command to get LED color configuration from device
/// </summary>
public class GetLedColorCommand
{
    private readonly ProtocolHandler _protocol;
    private readonly ILogger<GetLedColorCommand> _logger;

    public GetLedColorCommand(ProtocolHandler protocol, ILogger<GetLedColorCommand> logger)
    {
        _protocol = protocol;
        _logger = logger;
    }

    public async Task<LedConfig?> ExecuteAsync(byte profileId, byte buttonId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting LED color: Profile {ProfileId}, Button {ButtonId}", profileId, buttonId);

            var payload = new byte[] { profileId, buttonId };
            var response = await _protocol.SendCommandAsync(
                ProtocolConstants.CMD_GET_LED_COLOR,
                payload,
                cancellationToken: cancellationToken);

            if (response == null || response.Payload.Length < 6)
            {
                _logger.LogWarning("Invalid response for get LED color");
                return null;
            }

            if (response.Payload[0] != ProtocolConstants.STATUS_OK)
            {
                _logger.LogWarning("Device returned error status");
                return null;
            }

            var ledConfig = new LedConfig
            {
                R = response.Payload[1],
                G = response.Payload[2],
                B = response.Payload[3],
                Brightness = response.Payload[4],
                Effect = (LedEffect)response.Payload[5]
            };

            _logger.LogDebug("LED config: RGB({R},{G},{B}), Brightness: {Brightness}, Effect: {Effect}",
                ledConfig.R, ledConfig.G, ledConfig.B, ledConfig.Brightness, ledConfig.Effect);

            return ledConfig;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting LED color");
            return null;
        }
    }
}
