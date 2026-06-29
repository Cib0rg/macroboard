using MacroKeyboard.Communication.Protocol;
using MacroKeyboard.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace MacroKeyboard.Communication.Commands;

/// <summary>
/// Команда для передачи изображения на устройство.
/// 
/// Uses ProtocolHandler's locked session to hold the command lock for the
/// entire transfer (START + all chunks + END), preventing other commands
/// from interleaving and causing response misrouting.
/// </summary>
public class ImageTransferCommand
{
    private readonly ProtocolHandler _protocol;
    private readonly ILogger<ImageTransferCommand> _logger;
    
    public ImageTransferCommand(ProtocolHandler protocol, ILogger<ImageTransferCommand> logger)
    {
        _protocol = protocol;
        _logger = logger;
    }
    
    /// <summary>
    /// Отправить изображение на устройство.
    /// folderId: 0xFF = root button, 0..N = folder button.
    /// </summary>
    public async Task<bool> ExecuteAsync(
        byte profileId,
        byte buttonId,
        byte[] imageData,
        byte folderId = 0xFF,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default,
        bool noStore = false)
    {
        // Acquire the protocol lock for the entire image transfer session.
        // This prevents other commands (SetButtonAction, SetLedColor, etc.)
        // from interleaving with our START → chunks → END sequence.
        await _protocol.AcquireLockAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Starting image transfer for button {ButtonId}, size: {Size} bytes", 
                buttonId, imageData.Length);
            
            // 1. Start transfer
            var (startOk, transferId) = await StartTransferLockedAsync(profileId, buttonId, imageData, folderId, cancellationToken, noStore);
            if (!startOk)
            {
                _logger.LogError("Failed to start image transfer");
                return false;
            }
            
            // 2. Send chunks
            var totalChunks = (imageData.Length + ProtocolConstants.ImageChunkSize - 1) / ProtocolConstants.ImageChunkSize;
            
            for (int i = 0; i < totalChunks; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                    return false;
                
                var offset = i * ProtocolConstants.ImageChunkSize;
                var length = Math.Min(ProtocolConstants.ImageChunkSize, imageData.Length - offset);
                
                var chunk = new byte[length];
                Array.Copy(imageData, offset, chunk, 0, length);
                
                var success = await SendChunkLockedAsync((byte)transferId, (ushort)i, chunk, cancellationToken);
                if (!success)
                {
                    _logger.LogError("Failed to send chunk {ChunkNum}/{TotalChunks}", i + 1, totalChunks);
                    return false;
                }
                
                progress?.Report((i + 1) * 100 / totalChunks);
            }
            
            // 3. End transfer
            var crc32 = Crc32.Calculate(imageData);
            var completed = await EndTransferLockedAsync((byte)transferId, (uint)totalChunks, crc32, cancellationToken);
            
            if (completed)
            {
                _logger.LogInformation("Image transfer completed successfully for button {ButtonId}", buttonId);
            }
            else
            {
                _logger.LogError("Image transfer failed at completion stage");
            }
            
            return completed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during image transfer");
            return false;
        }
        finally
        {
            _protocol.ReleaseLock();
        }
    }
    
    private async Task<(bool success, ushort transferId)> StartTransferLockedAsync(
        byte profileId,
        byte buttonId,
        byte[] imageData,
        byte folderId,
        CancellationToken cancellationToken,
        bool noStore = false)
    {
        var payload = new byte[12];
        payload[0] = profileId;
        payload[1] = buttonId;

        var size = (uint)imageData.Length;
        payload[2] = (byte)(size & 0xFF);
        payload[3] = (byte)((size >> 8) & 0xFF);
        payload[4] = (byte)((size >> 16) & 0xFF);
        payload[5] = (byte)((size >> 24) & 0xFF);

        payload[6] = 0x01; // Format: JPEG
        // payload[7]: transfer flags. TRANSFER_FLAG_VOLATILE (0x01) = skip SPIFFS write.
        // Old firmware ignores this byte; bit 0 of the old "width=160" value is 0 → safe.
        payload[7] = noStore ? (byte)0x01 : (byte)0x00;
        payload[8] = 0;
        payload[9] = 0;
        payload[10] = 0;
        payload[11] = folderId; // 0xFF = root; 0..N = folder
        
        var response = await _protocol.SendCommandLockedAsync(
            ProtocolConstants.CMD_START_IMAGE_TRANSFER,
            payload,
            ProtocolConstants.ImageTransferTimeout,
            cancellationToken);
        
        if (response == null)
        {
            _logger.LogError("START_IMAGE_TRANSFER: no response received");
            return (false, 0);
        }
        
        var status = response.Payload[0];
        _logger.LogDebug("START_IMAGE_TRANSFER response: status=0x{Status:X2}, payload[1]=0x{P1:X2}, payload[2]=0x{P2:X2}, cmdId=0x{Cmd:X2}",
            status, response.Payload[1], response.Payload[2], response.CommandId);
        
        if (status != ProtocolConstants.STATUS_OK)
        {
            _logger.LogError("START_IMAGE_TRANSFER failed: status=0x{Status:X2} (expected 0x00)", status);
            return (false, 0);
        }
        
        var transferId = (ushort)(response.Payload[1] | (response.Payload[2] << 8));
        _logger.LogDebug("Image transfer started, transferId={TransferId}", transferId);
        return (true, transferId);
    }
    
    // Compact CHUNK layout: [tid u8][chunkNum u16 LE][dataLen u8][data…] — 4 bytes overhead → 52 bytes data.
    // No per-chunk ACK: packets are streamed without waiting for a response.
    // Integrity is guaranteed by CRC32 checked in the END command.
    private async Task<bool> SendChunkLockedAsync(
        byte transferId,
        ushort chunkNumber,
        byte[] chunkData,
        CancellationToken cancellationToken)
    {
        var payload = new byte[4 + chunkData.Length];

        payload[0] = transferId;                              // u8
        payload[1] = (byte)(chunkNumber & 0xFF);             // u16 LE
        payload[2] = (byte)((chunkNumber >> 8) & 0xFF);
        payload[3] = (byte)chunkData.Length;                 // u8

        Array.Copy(chunkData, 0, payload, 4, chunkData.Length);

        return await _protocol.WriteLockedAsync(
            ProtocolConstants.CMD_IMAGE_DATA_CHUNK,
            payload,
            cancellationToken);
    }
    
    // Compact END layout: [tid u8][totalChunks u32 LE][crc32 u32 LE] = 9 bytes
    private async Task<bool> EndTransferLockedAsync(
        byte transferId,
        uint totalChunks,
        uint crc32,
        CancellationToken cancellationToken)
    {
        var payload = new byte[9];

        payload[0] = transferId;                              // u8

        payload[1] = (byte)(totalChunks & 0xFF);
        payload[2] = (byte)((totalChunks >> 8) & 0xFF);
        payload[3] = (byte)((totalChunks >> 16) & 0xFF);
        payload[4] = (byte)((totalChunks >> 24) & 0xFF);

        payload[5] = (byte)(crc32 & 0xFF);
        payload[6] = (byte)((crc32 >> 8) & 0xFF);
        payload[7] = (byte)((crc32 >> 16) & 0xFF);
        payload[8] = (byte)((crc32 >> 24) & 0xFF);
        
        var response = await _protocol.SendCommandLockedAsync(
            ProtocolConstants.CMD_END_IMAGE_TRANSFER,
            payload,
            ProtocolConstants.ImageTransferTimeout,
            cancellationToken);
        
        if (response == null)
            return false;
        
        var status = response.Payload[0];
        var deviceCrc32 = BitConverter.ToUInt32(response.Payload, 1);

        if (status != ProtocolConstants.STATUS_OK)
        {
            _logger.LogError("Image transfer rejected by device (STATUS_ERROR). Device CRC32=0x{DeviceCrc:X8}, expected=0x{Expected:X8}",
                deviceCrc32, crc32);
            return false;
        }

        if (deviceCrc32 != crc32)
        {
            _logger.LogError("CRC mismatch: expected=0x{Expected:X8}, got=0x{Got:X8}", crc32, deviceCrc32);
            return false;
        }

        return true;
    }
}
