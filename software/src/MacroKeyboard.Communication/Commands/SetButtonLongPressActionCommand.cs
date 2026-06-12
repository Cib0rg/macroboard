using MacroKeyboard.Communication.Protocol;
using MacroKeyboard.Core.Models;
using Microsoft.Extensions.Logging;

namespace MacroKeyboard.Communication.Commands;

/// <summary>
/// CMD_SET_BUTTON_LONG_PRESS_ACTION — set long press action for a button
/// Payload: [button_id(1)][action_type(1)][data_len(2 LE)][data...]
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

    public async Task<bool> ExecuteAsync(byte buttonId, ActionConfig? action, CancellationToken cancellationToken = default)
    {
        try
        {
            var actionData = action?.ToBytes() ?? Array.Empty<byte>();
            var actionType = (byte)(action?.ActionType ?? ActionType.None);

            var maxDataLen = ProtocolConstants.PayloadSize - 4;
            var actualLen = Math.Min(actionData.Length, maxDataLen);

            var payload = new byte[4 + actualLen];
            payload[0] = buttonId;
            payload[1] = actionType;
            payload[2] = (byte)(actualLen & 0xFF);
            payload[3] = (byte)((actualLen >> 8) & 0xFF);
            Array.Copy(actionData, 0, payload, 4, actualLen);

            var response = await _protocol.SendCommandAsync(
                ProtocolConstants.CMD_SET_BUTTON_LONG_PRESS_ACTION,
                payload,
                cancellationToken: cancellationToken);

            if (response == null) return false;

            bool ok = response.Payload[0] == ProtocolConstants.STATUS_OK;
            if (!ok)
                _logger.LogError("SetButtonLongPressAction button {Id} failed: 0x{Status:X2}", buttonId, response.Payload[0]);
            return ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting long press action for button {Id}", buttonId);
            return false;
        }
    }
}
