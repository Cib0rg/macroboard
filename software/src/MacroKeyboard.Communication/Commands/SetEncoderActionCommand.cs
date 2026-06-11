using MacroKeyboard.Communication.Protocol;
using MacroKeyboard.Core.Models;
using Microsoft.Extensions.Logging;

namespace MacroKeyboard.Communication.Commands;

/// <summary>
/// CMD_SET_ENCODER_ACTION — set action for an encoder slot (0=CW, 1=CCW, 2=press, 3=long press)
/// </summary>
public class SetEncoderActionCommand
{
    private readonly ProtocolHandler _protocol;
    private readonly ILogger<SetEncoderActionCommand> _logger;

    public SetEncoderActionCommand(ProtocolHandler protocol, ILogger<SetEncoderActionCommand> logger)
    {
        _protocol = protocol;
        _logger = logger;
    }

    public async Task<bool> ExecuteAsync(byte slot, ActionConfig? action, CancellationToken cancellationToken = default)
    {
        try
        {
            var actionData = action?.ToBytes() ?? Array.Empty<byte>();
            var actionType = (byte)(action?.ActionType ?? ActionType.None);

            // Clamp to fit: payload = [slot(1)][type(1)][len(2)][data...]
            var maxDataLen = ProtocolConstants.PayloadSize - 4;
            var actualLen = Math.Min(actionData.Length, maxDataLen);

            var payload = new byte[4 + actualLen];
            payload[0] = slot;
            payload[1] = actionType;
            payload[2] = (byte)(actualLen & 0xFF);
            payload[3] = (byte)((actualLen >> 8) & 0xFF);
            Array.Copy(actionData, 0, payload, 4, actualLen);

            var response = await _protocol.SendCommandAsync(
                ProtocolConstants.CMD_SET_ENCODER_ACTION,
                payload,
                cancellationToken: cancellationToken);

            if (response == null) return false;

            bool ok = response.Payload[0] == ProtocolConstants.STATUS_OK;
            if (!ok)
                _logger.LogError("SetEncoderAction slot {Slot} failed: 0x{Status:X2}", slot, response.Payload[0]);
            return ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting encoder action slot {Slot}", slot);
            return false;
        }
    }
}
