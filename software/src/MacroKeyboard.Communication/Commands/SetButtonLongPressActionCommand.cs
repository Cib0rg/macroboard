using MacroKeyboard.Communication.Protocol;
using MacroKeyboard.Core.Models;
using Microsoft.Extensions.Logging;

namespace MacroKeyboard.Communication.Commands;

/// <summary>
/// CMD_SET_BUTTON_LONG_PRESS_ACTION
/// Payload: [profile_id(1)][folder_id(1)][button_id(1)][action_type(1)][data_len(2 LE)][data...]
/// folder_id=0xFF → root buttons
/// </summary>
public class SetButtonLongPressActionCommand
{
    private readonly ProtocolHandler _protocol;
    private readonly ILogger<SetButtonLongPressActionCommand> _logger;

    public SetButtonLongPressActionCommand(ProtocolHandler protocol, ILogger<SetButtonLongPressActionCommand> logger)
    {
        _protocol = protocol;
        _logger = logger;
    }

    public async Task<bool> ExecuteAsync(
        byte profileId, byte buttonId, ActionConfig? action,
        byte folderId = 0xFF,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var actionData = action?.ToBytes() ?? Array.Empty<byte>();
            var actionType = (byte)(action?.ActionType ?? ActionType.None);

            var maxDataLen = ProtocolConstants.PayloadSize - 6;
            var actualLen = Math.Min(actionData.Length, maxDataLen);

            var payload = new byte[6 + actualLen];
            payload[0] = profileId;
            payload[1] = folderId;
            payload[2] = buttonId;
            payload[3] = actionType;
            payload[4] = (byte)(actualLen & 0xFF);
            payload[5] = (byte)((actualLen >> 8) & 0xFF);
            Array.Copy(actionData, 0, payload, 6, actualLen);

            var response = await _protocol.SendCommandAsync(
                ProtocolConstants.CMD_SET_BUTTON_LONG_PRESS_ACTION,
                payload,
                cancellationToken: cancellationToken);

            if (response == null) return false;

            bool ok = response.Payload[0] == ProtocolConstants.STATUS_OK;
            if (!ok)
                _logger.LogError("SetButtonLongPressAction f={FolderId} b={ButtonId} failed: 0x{Status:X2}",
                    folderId, buttonId, response.Payload[0]);
            return ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting long press action f={FolderId} b={ButtonId}", folderId, buttonId);
            return false;
        }
    }
}
