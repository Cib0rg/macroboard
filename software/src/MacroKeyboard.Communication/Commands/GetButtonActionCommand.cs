using MacroKeyboard.Communication.Protocol;
using MacroKeyboard.Core.Models;
using Microsoft.Extensions.Logging;

namespace MacroKeyboard.Communication.Commands;

/// <summary>
/// Command to get button action configuration from device
/// </summary>
public class GetButtonActionCommand
{
    private readonly ProtocolHandler _protocol;
    private readonly ILogger<GetButtonActionCommand> _logger;

    public GetButtonActionCommand(ProtocolHandler protocol, ILogger<GetButtonActionCommand> logger)
    {
        _protocol = protocol;
        _logger = logger;
    }

    public async Task<ActionConfig?> ExecuteAsync(byte profileId, byte buttonId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting button action: Profile {ProfileId}, Button {ButtonId}", profileId, buttonId);

            var payload = new byte[] { profileId, buttonId };
            var response = await _protocol.SendCommandAsync(
                ProtocolConstants.CMD_GET_BUTTON_ACTION,
                payload,
                cancellationToken: cancellationToken);

            if (response == null || response.Payload.Length < 4)
            {
                _logger.LogWarning("Invalid response for get button action");
                return null;
            }

            if (response.Payload[0] != ProtocolConstants.STATUS_OK)
            {
                _logger.LogWarning("Device returned error status");
                return null;
            }

            var actionType = (ActionType)response.Payload[1];
            var actionLen = BitConverter.ToUInt16(response.Payload, 2);
            
            _logger.LogDebug("Action type: {ActionType}, Length: {Length}", actionType, actionLen);

            // Parse action based on type
            return actionType switch
            {
                ActionType.Keyboard => ParseKeyboardAction(response.Payload, 4, actionLen),
                ActionType.ProfileSwitch => ParseProfileSwitchAction(response.Payload, 4, actionLen),
                ActionType.CustomHid => ParseCustomHidAction(response.Payload, 4, actionLen),
                ActionType.None => null,
                _ => null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting button action");
            return null;
        }
    }

    private KeyboardAction? ParseKeyboardAction(byte[] data, int offset, int length)
    {
        if (length < 3)
            return null;

        return new KeyboardAction
        {
            KeyCode = data[offset],
            Modifiers = (KeyModifiers)data[offset + 1],
            Text = length > 3 ? System.Text.Encoding.UTF8.GetString(data, offset + 3, length - 3) : null
        };
    }

    private ProfileSwitchAction? ParseProfileSwitchAction(byte[] data, int offset, int length)
    {
        if (length < 1)
            return null;

        return new ProfileSwitchAction
        {
            TargetProfileId = data[offset]
        };
    }

    private CustomHidAction? ParseCustomHidAction(byte[] data, int offset, int length)
    {
        var actionData = new byte[length];
        Array.Copy(data, offset, actionData, 0, length);

        return new CustomHidAction
        {
            Data = actionData
        };
    }
}
