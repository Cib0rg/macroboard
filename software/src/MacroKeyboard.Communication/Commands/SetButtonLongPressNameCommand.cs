using System.Text;
using MacroKeyboard.Communication.Protocol;
using Microsoft.Extensions.Logging;

namespace MacroKeyboard.Communication.Commands;

/// <summary>
/// CMD_SET_BUTTON_LONG_PRESS_NAME — set the label shown in the long-press section of the split display.
/// Payload: [button_id (1)] [name bytes (UTF-8, no null needed, max 31)]
/// Empty name makes the firmware auto-generate the label from the action type.
/// </summary>
public class SetButtonLongPressNameCommand
{
    private readonly ProtocolHandler _protocol;
    private readonly ILogger<SetButtonLongPressNameCommand> _logger;

    public SetButtonLongPressNameCommand(ProtocolHandler protocol, ILogger<SetButtonLongPressNameCommand> logger)
    {
        _protocol = protocol;
        _logger = logger;
    }

    public async Task<bool> ExecuteAsync(
        byte buttonId,
        string? name,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var nameBytes = string.IsNullOrEmpty(name)
                ? Array.Empty<byte>()
                : Encoding.UTF8.GetBytes(name);

            const int MaxNameBytes = 31;
            if (nameBytes.Length > MaxNameBytes)
                nameBytes = nameBytes[..MaxNameBytes];

            var payload = new byte[1 + nameBytes.Length];
            payload[0] = buttonId;
            nameBytes.CopyTo(payload, 1);

            var response = await _protocol.SendCommandAsync(
                ProtocolConstants.CMD_SET_BUTTON_LONG_PRESS_NAME,
                payload,
                cancellationToken: cancellationToken);

            if (response == null) return false;

            bool ok = response.Payload[0] == ProtocolConstants.STATUS_OK;
            if (!ok)
                _logger.LogError("SetButtonLongPressName button {Id} failed: 0x{Status:X2}", buttonId, response.Payload[0]);
            return ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting long press name for button {Id}", buttonId);
            return false;
        }
    }
}
