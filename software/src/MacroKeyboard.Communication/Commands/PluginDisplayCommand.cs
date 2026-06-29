using MacroKeyboard.Communication.Protocol;
using System.Text;

namespace MacroKeyboard.Communication.Commands;

/// <summary>
/// CMD_PLUGIN_DISPLAY (0x44): ask the firmware to render a ring + text label on a button display.
///
/// Payload layout: [button_id(1)][folder_id(1)][flags(1)][text_len(1)][text(0..52)]
///   folder_id: 0xFF = root button; 0x00-0x0F = button inside that folder
///   flags bit 0: isOn — 1 = green ring (#22C55E), 0 = purple ring (#8B5CF6)
///
/// The firmware handles all rendering (no JPEG, no SPIFFS). A single packet replaces
/// the ~6500-byte JPEG image transfer used previously for plugin display updates.
/// </summary>
public class PluginDisplayCommand
{
    private readonly ProtocolHandler _protocol;

    public PluginDisplayCommand(ProtocolHandler protocol) => _protocol = protocol;

    public async Task<bool> ExecuteAsync(byte buttonId, byte folderId, string text, bool isOn,
        CancellationToken ct = default)
    {
        var textBytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
        // Max text = PayloadSize(56) - 4 header bytes = 52 bytes
        var maxText = ProtocolConstants.PayloadSize - 4;
        if (textBytes.Length > maxText)
            textBytes = textBytes[..maxText];

        var payload = new byte[4 + textBytes.Length];
        payload[0] = buttonId;
        payload[1] = folderId;                      // 0xFF = root, 0x00-0x0F = folder
        payload[2] = (byte)(isOn ? 0x01 : 0x00);   // flags: bit 0 = isOn
        payload[3] = (byte)textBytes.Length;
        Array.Copy(textBytes, 0, payload, 4, textBytes.Length);

        var response = await _protocol.SendCommandAsync(
            ProtocolConstants.CMD_PLUGIN_DISPLAY,
            payload,
            ProtocolConstants.DefaultTimeout,
            ct);

        return response?.Payload[0] == ProtocolConstants.STATUS_OK;
    }
}
