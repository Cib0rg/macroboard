using MacroKeyboard.Communication.Protocol;
using MacroKeyboard.Core.Models;
using Microsoft.Extensions.Logging;

namespace MacroKeyboard.Communication.Commands;

public class SetFolderButtonActionCommand
{
    private readonly ProtocolHandler _protocol;
    private readonly ILogger<SetFolderButtonActionCommand> _logger;

    public SetFolderButtonActionCommand(ProtocolHandler protocol, ILogger<SetFolderButtonActionCommand> logger)
    {
        _protocol = protocol;
        _logger = logger;
    }

    public async Task<bool> ExecuteAsync(
        byte profileId, byte folderId, byte buttonId,
        ActionConfig action,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var actionData = action.ToBytes();
            var maxActionDataLen = ProtocolConstants.PayloadSize - 6;
            var actualActionLen = Math.Min(actionData.Length, maxActionDataLen);

            var payload = new byte[6 + actualActionLen];
            payload[0] = profileId;
            payload[1] = folderId;
            payload[2] = buttonId;
            payload[3] = (byte)action.ActionType;
            payload[4] = (byte)(actualActionLen & 0xFF);
            payload[5] = (byte)((actualActionLen >> 8) & 0xFF);
            Array.Copy(actionData, 0, payload, 6, actualActionLen);

            var response = await _protocol.SendCommandAsync(
                ProtocolConstants.CMD_SET_FOLDER_BUTTON_ACTION,
                payload,
                cancellationToken: cancellationToken);

            return response?.Payload[0] == ProtocolConstants.STATUS_OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting folder button action f={FolderId} b={ButtonId}", folderId, buttonId);
            return false;
        }
    }
}
