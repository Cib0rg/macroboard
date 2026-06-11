using MacroKeyboard.Communication.Protocol;
using Microsoft.Extensions.Logging;

namespace MacroKeyboard.Communication.Commands;

/// <summary>
/// CMD_DELETE_PROFILE (0x52) — deletes a profile's binary file from device SPIFFS
/// </summary>
public class DeleteProfileCommand
{
    private readonly ProtocolHandler _protocol;
    private readonly ILogger<DeleteProfileCommand> _logger;

    public DeleteProfileCommand(ProtocolHandler protocol, ILogger<DeleteProfileCommand> logger)
    {
        _protocol = protocol;
        _logger = logger;
    }

    public async Task<bool> ExecuteAsync(byte profileId, CancellationToken cancellationToken = default)
    {
        if (profileId >= 5)
        {
            _logger.LogError("Invalid profile ID: {ProfileId}", profileId);
            return false;
        }

        var payload = new byte[] { profileId };
        var response = await _protocol.SendCommandAsync(
            ProtocolConstants.CMD_DELETE_PROFILE,
            payload,
            cancellationToken: cancellationToken);

        if (response == null)
            return false;

        var status = response.Payload[0];
        if (status == ProtocolConstants.STATUS_OK)
        {
            _logger.LogInformation("Profile {ProfileId} deleted from device", profileId);
            return true;
        }

        _logger.LogError("Failed to delete profile {ProfileId} from device. Status: 0x{Status:X2}", profileId, status);
        return false;
    }
}
