using System.Text;
using MacroKeyboard.Communication.Protocol;
using Microsoft.Extensions.Logging;

namespace MacroKeyboard.Communication.Commands;

/// <summary>
/// CMD_SET_BUTTON_LONG_PRESS_NAME
/// Payload: [profile_id(1)][folder_id(1)][button_id(1)][name (UTF-8, max PayloadSize-3 bytes)]
/// folder_id=0xFF → root buttons. Empty name → firmware auto-generates from action type.
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
        byte profileId, byte buttonId, string? name,
        byte folderId = 0xFF,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var nameBytes = string.IsNullOrEmpty(name)
                ? Array.Empty<byte>()
                : Encoding.UTF8.GetBytes(name);

            var maxNameLen = ProtocolConstants.PayloadSize - 3;
            var actualLen = Math.Min(nameBytes.Length, maxNameLen);

            var payload = new byte[3 + actualLen];
            payload[0] = profileId;
            payload[1] = folderId;
            payload[2] = buttonId;
            Array.Copy(nameBytes, 0, payload, 3, actualLen);

            var response = await _protocol.SendCommandAsync(
                ProtocolConstants.CMD_SET_BUTTON_LONG_PRESS_NAME,
                payload,
                cancellationToken: cancellationToken);

            if (response == null) return false;

            bool ok = response.Payload[0] == ProtocolConstants.STATUS_OK;
            if (!ok)
                _logger.LogError("SetButtonLongPressName f={FolderId} b={ButtonId} failed: 0x{Status:X2}",
                    folderId, buttonId, response.Payload[0]);
            return ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting long press name f={FolderId} b={ButtonId}", folderId, buttonId);
            return false;
        }
    }
}
