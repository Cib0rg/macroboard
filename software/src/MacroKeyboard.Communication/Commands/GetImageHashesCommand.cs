using MacroKeyboard.Communication.Protocol;
using Microsoft.Extensions.Logging;

namespace MacroKeyboard.Communication.Commands;

/// <summary>
/// Запрашивает у устройства CRC32 хеши изображений всех кнопок указанного профиля.
/// Response: [status(1)][count(1)][{button_id(1), crc32_le(4)} × count]
/// </summary>
public class GetImageHashesCommand
{
    private readonly ProtocolHandler _protocol;
    private readonly ILogger<GetImageHashesCommand> _logger;

    public GetImageHashesCommand(ProtocolHandler protocol, ILogger<GetImageHashesCommand> logger)
    {
        _protocol = protocol;
        _logger = logger;
    }

    /// <summary>
    /// Returns a map of buttonId → CRC32 for all buttons that have an image in the given profile.
    /// Returns null on communication error.
    /// </summary>
    public async Task<Dictionary<byte, uint>?> ExecuteAsync(byte profileId, CancellationToken cancellationToken = default)
    {
        var response = await _protocol.SendCommandAsync(
            ProtocolConstants.CMD_GET_IMAGE_HASHES,
            new byte[] { profileId },
            cancellationToken: cancellationToken);

        if (response == null)
        {
            _logger.LogError("GET_IMAGE_HASHES: no response from device");
            return null;
        }

        if (response.Payload[0] != ProtocolConstants.STATUS_OK)
        {
            _logger.LogError("GET_IMAGE_HASHES: device returned error 0x{Status:X2}", response.Payload[0]);
            return null;
        }

        var count = response.Payload[1];
        var result = new Dictionary<byte, uint>(count);

        for (int i = 0; i < count; i++)
        {
            var offset = 2 + i * 5;
            if (offset + 5 > response.Payload.Length) break;
            var buttonId = response.Payload[offset];
            var crc = BitConverter.ToUInt32(response.Payload, offset + 1);
            result[buttonId] = crc;
            _logger.LogDebug("  button {ButtonId}: CRC=0x{Crc:X8}", buttonId, crc);
        }

        _logger.LogInformation("GET_IMAGE_HASHES profile={ProfileId}: {Count} entries", profileId, result.Count);
        return result;
    }
}
