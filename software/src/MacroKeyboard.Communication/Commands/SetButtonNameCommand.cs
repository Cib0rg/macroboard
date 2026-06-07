using System.Text;
using MacroKeyboard.Communication.Protocol;
using Microsoft.Extensions.Logging;

namespace MacroKeyboard.Communication.Commands;

/// <summary>
/// Команда SET_BUTTON_NAME — устанавливает отображаемое имя кнопки на устройстве.
/// Имя показывается на дисплее кнопки, если для неё не задана картинка.
/// </summary>
public class SetButtonNameCommand
{
    private readonly ProtocolHandler _protocol;
    private readonly ILogger<SetButtonNameCommand> _logger;

    public SetButtonNameCommand(ProtocolHandler protocol, ILogger<SetButtonNameCommand> logger)
    {
        _protocol = protocol;
        _logger = logger;
    }

    /// <summary>
    /// Отправить имя кнопки на устройство.
    /// </summary>
    /// <param name="profileId">ID профиля</param>
    /// <param name="buttonId">ID кнопки (0-9)</param>
    /// <param name="name">Имя кнопки (макс. 31 символ UTF-8); null или пустая строка очищает имя</param>
    public async Task<bool> ExecuteAsync(
        byte profileId,
        byte buttonId,
        string? name,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var nameBytes = string.IsNullOrEmpty(name)
                ? Array.Empty<byte>()
                : Encoding.UTF8.GetBytes(name);

            // Clamp to 31 bytes so the firmware always has room for a null terminator
            const int MaxNameBytes = 31;
            if (nameBytes.Length > MaxNameBytes)
                nameBytes = nameBytes[..MaxNameBytes];

            // payload: [profileId][buttonId][name bytes (no null terminator needed — firmware adds it)]
            var payload = new byte[2 + nameBytes.Length];
            payload[0] = profileId;
            payload[1] = buttonId;
            nameBytes.CopyTo(payload, 2);

            var response = await _protocol.SendCommandAsync(
                ProtocolConstants.CMD_SET_BUTTON_NAME,
                payload,
                cancellationToken: cancellationToken);

            if (response == null)
                return false;

            if (response.Payload[0] == ProtocolConstants.STATUS_OK)
            {
                _logger.LogInformation("Button name set for button {ButtonId}: '{Name}'", buttonId, name);
                return true;
            }

            _logger.LogError("Failed to set button name. Status: 0x{Status:X2}", response.Payload[0]);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting button name");
            return false;
        }
    }
}
