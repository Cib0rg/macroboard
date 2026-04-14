using MacroKeyboard.Communication.Protocol;
using MacroKeyboard.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace MacroKeyboard.Communication.Commands;

/// <summary>
/// Команда для передачи изображения на устройство
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
    /// Отправить изображение на устройство
    /// </summary>
    public async Task<bool> ExecuteAsync(
        byte profileId, 
        byte buttonId, 
        byte[] imageData,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Начать передачу
            _logger.LogInformation("Starting image transfer for button {ButtonId}, size: {Size} bytes", 
                buttonId, imageData.Length);
            
            var transferId = await StartTransferAsync(profileId, buttonId, imageData, cancellationToken);
            if (transferId == 0)
            {
                _logger.LogError("Failed to start image transfer");
                return false;
            }
            
            // 2. Отправить фрагменты
            var totalChunks = (imageData.Length + ProtocolConstants.ImageChunkSize - 1) / ProtocolConstants.ImageChunkSize;
            
            for (int i = 0; i < totalChunks; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                    return false;
                
                var offset = i * ProtocolConstants.ImageChunkSize;
                var length = Math.Min(ProtocolConstants.ImageChunkSize, imageData.Length - offset);
                
                var chunk = new byte[length];
                Array.Copy(imageData, offset, chunk, 0, length);
                
                var success = await SendChunkAsync(transferId, (ushort)i, chunk, cancellationToken);
                if (!success)
                {
                    _logger.LogError("Failed to send chunk {ChunkNum}/{TotalChunks}", i + 1, totalChunks);
                    return false;
                }
                
                // Обновить прогресс
                progress?.Report((i + 1) * 100 / totalChunks);
            }
            
            // 3. Завершить передачу
            var crc32 = Crc32.Calculate(imageData);
            var completed = await EndTransferAsync(transferId, (uint)totalChunks, crc32, cancellationToken);
            
            if (completed)
            {
                _logger.LogInformation("Image transfer completed successfully");
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
    }
    
    private async Task<ushort> StartTransferAsync(
        byte profileId, 
        byte buttonId, 
        byte[] imageData,
        CancellationToken cancellationToken)
    {
        var payload = new byte[11];
        payload[0] = profileId;
        payload[1] = buttonId;
        
        // Image size (little-endian)
        var size = (uint)imageData.Length;
        payload[2] = (byte)(size & 0xFF);
        payload[3] = (byte)((size >> 8) & 0xFF);
        payload[4] = (byte)((size >> 16) & 0xFF);
        payload[5] = (byte)((size >> 24) & 0xFF);
        
        payload[6] = 0x01; // Format: JPEG
        
        // Width and height (160x160)
        payload[7] = 160;
        payload[8] = 0;
        payload[9] = 160;
        payload[10] = 0;
        
        var response = await _protocol.SendCommandAsync(
            ProtocolConstants.CMD_START_IMAGE_TRANSFER,
            payload,
            ProtocolConstants.ImageTransferTimeout,
            cancellationToken);
        
        if (response == null)
            return 0;
        
        var status = response.Payload[0];
        if (status != ProtocolConstants.STATUS_OK)
            return 0;
        
        // Transfer ID (little-endian)
        var transferId = (ushort)(response.Payload[1] | (response.Payload[2] << 8));
        return transferId;
    }
    
    private async Task<bool> SendChunkAsync(
        ushort transferId, 
        ushort chunkNumber, 
        byte[] chunkData,
        CancellationToken cancellationToken)
    {
        var payload = new byte[6 + chunkData.Length];
        
        // Transfer ID
        payload[0] = (byte)(transferId & 0xFF);
        payload[1] = (byte)((transferId >> 8) & 0xFF);
        
        // Chunk number
        payload[2] = (byte)(chunkNumber & 0xFF);
        payload[3] = (byte)((chunkNumber >> 8) & 0xFF);
        
        // Chunk size
        payload[4] = (byte)(chunkData.Length & 0xFF);
        payload[5] = (byte)((chunkData.Length >> 8) & 0xFF);
        
        // Chunk data
        Array.Copy(chunkData, 0, payload, 6, chunkData.Length);
        
        var response = await _protocol.SendCommandAsync(
            ProtocolConstants.CMD_IMAGE_DATA_CHUNK,
            payload,
            ProtocolConstants.ImageTransferTimeout,
            cancellationToken);
        
        if (response == null)
            return false;
        
        var status = response.Payload[0];
        return status == ProtocolConstants.STATUS_OK;
    }
    
    private async Task<bool> EndTransferAsync(
        ushort transferId, 
        uint totalChunks, 
        uint crc32,
        CancellationToken cancellationToken)
    {
        var payload = new byte[10];
        
        // Transfer ID
        payload[0] = (byte)(transferId & 0xFF);
        payload[1] = (byte)((transferId >> 8) & 0xFF);
        
        // Total chunks
        payload[2] = (byte)(totalChunks & 0xFF);
        payload[3] = (byte)((totalChunks >> 8) & 0xFF);
        payload[4] = (byte)((totalChunks >> 16) & 0xFF);
        payload[5] = (byte)((totalChunks >> 24) & 0xFF);
        
        // CRC32
        payload[6] = (byte)(crc32 & 0xFF);
        payload[7] = (byte)((crc32 >> 8) & 0xFF);
        payload[8] = (byte)((crc32 >> 16) & 0xFF);
        payload[9] = (byte)((crc32 >> 24) & 0xFF);
        
        var response = await _protocol.SendCommandAsync(
            ProtocolConstants.CMD_END_IMAGE_TRANSFER,
            payload,
            ProtocolConstants.ImageTransferTimeout,
            cancellationToken);
        
        if (response == null)
            return false;
        
        var status = response.Payload[0];
        
        // Проверить CRC
        var deviceCrc32 = BitConverter.ToUInt32(response.Payload, 1);
        
        if (status == ProtocolConstants.STATUS_OK && deviceCrc32 == crc32)
        {
            return true;
        }
        
        _logger.LogError("CRC mismatch. Expected: 0x{Expected:X8}, Got: 0x{Got:X8}", crc32, deviceCrc32);
        return false;
    }
}
