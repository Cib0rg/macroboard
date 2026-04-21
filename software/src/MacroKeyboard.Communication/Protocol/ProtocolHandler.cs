using Microsoft.Extensions.Logging;
using MacroKeyboard.Communication.HidDevice;

namespace MacroKeyboard.Communication.Protocol;

/// <summary>
/// Обработчик протокола обмена данными
/// </summary>
public class ProtocolHandler
{
    private readonly HidDeviceManager _deviceManager;
    private readonly ILogger<ProtocolHandler> _logger;
    private ushort _sequenceNumber;
    
    public ProtocolHandler(HidDeviceManager deviceManager, ILogger<ProtocolHandler> logger)
    {
        _deviceManager = deviceManager;
        _logger = logger;
    }
    
    /// <summary>
    /// Отправить команду и получить ответ.
    /// 
    /// IMPORTANT: The response waiter is registered BEFORE writing the command
    /// to avoid a race condition where the monitor thread reads the response
    /// before ReadAsync has a chance to register its TaskCompletionSource.
    /// </summary>
    public async Task<ProtocolPacket?> SendCommandAsync(
        byte commandId, 
        byte[] payload, 
        int timeoutMs = ProtocolConstants.DefaultTimeout,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Создать пакет
            var packet = new ProtocolPacket
            {
                CommandId = commandId,
                PayloadLength = (ushort)payload.Length,
                SequenceNumber = _sequenceNumber++,
                Payload = new byte[ProtocolConstants.PayloadSize]
            };
            
            Array.Copy(payload, 0, packet.Payload, 0, Math.Min(payload.Length, ProtocolConstants.PayloadSize));
            
            var packetBytes = packet.ToBytes();
            
            _logger.LogDebug("Sending command 0x{CommandId:X2}, seq: {Seq}", commandId, packet.SequenceNumber);
            
            // Step 1: Register response waiter BEFORE writing (prevents race condition)
            var responseTask = _deviceManager.WaitForResponseAsync(timeoutMs);
            
            // Step 2: Write command
            var sent = await _deviceManager.WriteAsync(packetBytes);
            if (!sent)
            {
                _logger.LogError("Failed to send command");
                // Cancel the pending waiter since we didn't send
                _deviceManager.CancelPendingResponse(responseTask);
                return null;
            }
            
            // Step 3: Wait for response (TCS was already registered, monitor thread will complete it)
            var responseData = await responseTask;
            if (responseData == null)
            {
                _logger.LogWarning("No response received for command 0x{CommandId:X2}", commandId);
                return null;
            }
            
            var response = ProtocolPacket.FromBytes(responseData);
            if (response == null)
            {
                _logger.LogError("Invalid response packet");
                return null;
            }
            
            _logger.LogDebug("Received response for command 0x{CommandId:X2}", commandId);
            
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending command 0x{CommandId:X2}", commandId);
            return null;
        }
    }
    
    /// <summary>
    /// Отправить команду без ожидания ответа
    /// </summary>
    public async Task<bool> SendCommandNoResponseAsync(
        byte commandId, 
        byte[] payload,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var packet = new ProtocolPacket
            {
                CommandId = commandId,
                PayloadLength = (ushort)payload.Length,
                SequenceNumber = _sequenceNumber++,
                Payload = new byte[ProtocolConstants.PayloadSize]
            };
            
            Array.Copy(payload, 0, packet.Payload, 0, Math.Min(payload.Length, ProtocolConstants.PayloadSize));
            
            var packetBytes = packet.ToBytes();
            return await _deviceManager.WriteAsync(packetBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending command 0x{CommandId:X2}", commandId);
            return false;
        }
    }
}
