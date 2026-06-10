using MacroKeyboard.Communication.Protocol;
using MacroKeyboard.Core.Models;
using Microsoft.Extensions.Logging;

namespace MacroKeyboard.Communication.Commands;

public class SetFolderButtonLedCommand
{
    private readonly ProtocolHandler _protocol;
    private readonly ILogger<SetFolderButtonLedCommand> _logger;

    public SetFolderButtonLedCommand(ProtocolHandler protocol, ILogger<SetFolderButtonLedCommand> logger)
    {
        _protocol = protocol;
        _logger = logger;
    }

    public async Task<bool> ExecuteAsync(
        byte profileId, byte folderId, byte buttonId,
        LedConfig led,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new byte[]
            {
                profileId, folderId, buttonId,
                led.R, led.G, led.B, led.Brightness,
                (byte)(led.Effect)
            };

            var response = await _protocol.SendCommandAsync(
                ProtocolConstants.CMD_SET_FOLDER_BUTTON_LED,
                payload,
                cancellationToken: cancellationToken);

            return response?.Payload[0] == ProtocolConstants.STATUS_OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting folder button LED f={FolderId} b={ButtonId}", folderId, buttonId);
            return false;
        }
    }
}
