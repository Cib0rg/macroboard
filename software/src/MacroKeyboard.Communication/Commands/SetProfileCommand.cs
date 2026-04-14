using MacroKeyboard.Communication.Protocol;
using Microsoft.Extensions.Logging;

namespace MacroKeyboard.Communication.Commands;

/// <summary>
/// Команда SET_PROFILE для переключения активного профиля
/// </summary>
public class SetProfileCommand
{
    private readonly ProtocolHandler _protocol;
    private readonly ILogger<SetProfileCommand> _logger;
    
    public SetProfileCommand(ProtocolHandler protocol, ILogger<SetProfileCommand> logger)
    {
        _protocol = protocol;
        _logger = logger;
    }
    
    /// <summary>
    /// Выполнить команду SET_PROFILE
    /// </summary>
    public async Task<bool> ExecuteAsync(byte profileId, CancellationToken cancellationToken = default)
    {
        if (profileId > 4)
        {
            _logger.LogError("Invalid profile ID: {ProfileId}", profileId);
            return false;
        }
        
        var payload = new byte[] { profileId };
        
        var response = await _protocol.SendCommandAsync(
            ProtocolConstants.CMD_SET_PROFILE,
            payload,
            cancellationToken: cancellationToken);
        
        if (response == null)
            return false;
        
        // Проверить статус
        var status = response.Payload[0];
        var currentProfile = response.Payload[1];
        
        if (status == ProtocolConstants.STATUS_OK && currentProfile == profileId)
        {
            _logger.LogInformation("Profile switched to {ProfileId}", profileId);
            return true;
        }
        
        _logger.LogError("Failed to switch profile. Status: 0x{Status:X2}", status);
        return false;
    }
}
