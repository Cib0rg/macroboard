using MacroKeyboard.Communication.Protocol;
using Microsoft.Extensions.Logging;
using System.Text;

namespace MacroKeyboard.Communication.Commands;

public class SetFolderButtonNameCommand
{
    private readonly ProtocolHandler _protocol;
    private readonly ILogger<SetFolderButtonNameCommand> _logger;

    public SetFolderButtonNameCommand(ProtocolHandler protocol, ILogger<SetFolderButtonNameCommand> logger)
    {
        _protocol = protocol;
        _logger = logger;
    }

    public async Task<bool> ExecuteAsync(
        byte profileId, byte folderId, byte buttonId,
        string? name,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var nameBytes = name != null ? Encoding.UTF8.GetBytes(name) : Array.Empty<byte>();
            var maxNameLen = ProtocolConstants.PayloadSize - 3;
            if (nameBytes.Length > maxNameLen) nameBytes = nameBytes[..maxNameLen];

            var payload = new byte[3 + nameBytes.Length];
            payload[0] = profileId;
            payload[1] = folderId;
            payload[2] = buttonId;
            Array.Copy(nameBytes, 0, payload, 3, nameBytes.Length);

            var response = await _protocol.SendCommandAsync(
                ProtocolConstants.CMD_SET_FOLDER_BUTTON_NAME,
                payload,
                cancellationToken: cancellationToken);

            return response?.Payload[0] == ProtocolConstants.STATUS_OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting folder button name f={FolderId} b={ButtonId}", folderId, buttonId);
            return false;
        }
    }
}
