using MacroKeyboard.Communication.Protocol;
using MacroKeyboard.Core.Models;
using Microsoft.Extensions.Logging;

namespace MacroKeyboard.Communication.Commands;

/// <summary>
/// Команда SET_BUTTON_ACTION для установки действия кнопки
/// </summary>
public class SetButtonActionCommand
{
    private readonly ProtocolHandler _protocol;
    private readonly ILogger<SetButtonActionCommand> _logger;
    
    public SetButtonActionCommand(ProtocolHandler protocol, ILogger<SetButtonActionCommand> logger)
    {
        _protocol = protocol;
        _logger = logger;
    }
    
    /// <summary>
    /// Выполнить команду SET_BUTTON_ACTION
    /// </summary>
    public async Task<bool> ExecuteAsync(
        byte profileId, 
        byte buttonId, 
        ActionConfig action,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var actionData = action.ToBytes();
            
            // Clamp action data to fit within protocol payload (56 bytes total, 5 bytes header)
            var maxActionDataLen = ProtocolConstants.PayloadSize - 5;
            var actualActionLen = Math.Min(actionData.Length, maxActionDataLen);
            
            if (actionData.Length > maxActionDataLen)
            {
                _logger.LogWarning("Action data truncated: {Original} → {Actual} bytes (max payload: {Max})",
                    actionData.Length, actualActionLen, ProtocolConstants.PayloadSize);
            }
            
            var payload = new byte[5 + actualActionLen];
            payload[0] = profileId;
            payload[1] = buttonId;
            payload[2] = (byte)action.ActionType;
            
            // Action data length (little-endian) — use actual (possibly truncated) length
            payload[3] = (byte)(actualActionLen & 0xFF);
            payload[4] = (byte)((actualActionLen >> 8) & 0xFF);
            
            // Action data
            Array.Copy(actionData, 0, payload, 5, actualActionLen);
            
            var response = await _protocol.SendCommandAsync(
                ProtocolConstants.CMD_SET_BUTTON_ACTION,
                payload,
                cancellationToken: cancellationToken);
            
            if (response == null)
                return false;
            
            var status = response.Payload[0];
            
            if (status == ProtocolConstants.STATUS_OK)
            {
                _logger.LogInformation("Button action set for button {ButtonId}", buttonId);
                return true;
            }
            
            _logger.LogError("Failed to set button action. Status: 0x{Status:X2}", status);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting button action");
            return false;
        }
    }
}
