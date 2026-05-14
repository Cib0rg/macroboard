using Microsoft.Extensions.Logging;
using MacroKeyboard.Communication.HidDevice;

namespace MacroKeyboard.Communication.Protocol;

/// <summary>
/// Обработчик протокола обмена данными.
/// 
/// Uses a SemaphoreSlim to serialize all command-response exchanges.
/// Only one command can be in flight at a time, which guarantees that
/// the FIFO response routing in HidDeviceManager always delivers the
/// correct response to the correct caller.
/// 
/// For multi-command operations (like image transfer: START + chunks + END),
/// callers can use AcquireLockAsync/ReleaseLock to hold the lock for the
/// entire session, then use SendCommandLockedAsync for individual commands.
/// </summary>
public class ProtocolHandler
{
    private readonly HidDeviceManager _deviceManager;
    private readonly ILogger<ProtocolHandler> _logger;
    private ushort _sequenceNumber;
    
    /// <summary>
    /// Serializes command-response exchanges so only one is in flight at a time.
    /// </summary>
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    
    public ProtocolHandler(HidDeviceManager deviceManager, ILogger<ProtocolHandler> logger)
    {
        _deviceManager = deviceManager;
        _logger = logger;
    }
    
    /// <summary>
    /// Acquire the command lock for a multi-command session (e.g., image transfer).
    /// Must be paired with ReleaseLock(). Use SendCommandLockedAsync() for commands
    /// within the locked session.
    /// </summary>
    public async Task AcquireLockAsync(CancellationToken cancellationToken = default)
    {
        await _commandLock.WaitAsync(cancellationToken);
    }
    
    /// <summary>
    /// Release the command lock after a multi-command session.
    /// </summary>
    public void ReleaseLock()
    {
        _commandLock.Release();
    }
    
    /// <summary>
    /// Send a command while the lock is already held (within AcquireLock/ReleaseLock session).
    /// Does NOT acquire the lock — caller must hold it.
    /// </summary>
    public async Task<ProtocolPacket?> SendCommandLockedAsync(
        byte commandId,
        byte[] payload,
        int timeoutMs = ProtocolConstants.DefaultTimeout,
        CancellationToken cancellationToken = default)
    {
        return await SendCommandInternalAsync(commandId, payload, timeoutMs, cancellationToken);
    }
    
    /// <summary>
    /// Отправить команду и получить ответ.
    /// Thread-safe: acquires _commandLock automatically.
    /// </summary>
    public async Task<ProtocolPacket?> SendCommandAsync(
        byte commandId, 
        byte[] payload, 
        int timeoutMs = ProtocolConstants.DefaultTimeout,
        CancellationToken cancellationToken = default)
    {
        await _commandLock.WaitAsync(cancellationToken);
        try
        {
            return await SendCommandInternalAsync(commandId, payload, timeoutMs, cancellationToken);
        }
        finally
        {
            _commandLock.Release();
        }
    }
    
    /// <summary>
    /// Internal implementation — does NOT acquire the lock.
    /// </summary>
    private async Task<ProtocolPacket?> SendCommandInternalAsync(
        byte commandId,
        byte[] payload,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        try
        {
            var actualPayloadLength = Math.Min(payload.Length, ProtocolConstants.PayloadSize);
            var packet = new ProtocolPacket
            {
                CommandId = commandId,
                PayloadLength = (ushort)actualPayloadLength,
                SequenceNumber = _sequenceNumber++,
                Payload = new byte[ProtocolConstants.PayloadSize]
            };
            
            Array.Copy(payload, 0, packet.Payload, 0, actualPayloadLength);
            
            var packetBytes = packet.ToBytes();
            
            _logger.LogDebug("Sending command 0x{CommandId:X2}, seq: {Seq}", commandId, packet.SequenceNumber);
            
            // Step 1: Register response waiter BEFORE writing
            var responseTask = _deviceManager.WaitForResponseAsync(timeoutMs);
            
            // Step 2: Write command
            var sent = await _deviceManager.WriteAsync(packetBytes);
            if (!sent)
            {
                _logger.LogError("Failed to send command");
                _deviceManager.CancelPendingResponse(responseTask);
                return null;
            }
            
            // Step 3: Wait for response
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
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Command 0x{CommandId:X2} cancelled", commandId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending command 0x{CommandId:X2}", commandId);
            return null;
        }
    }
    
    /// <summary>
    /// Отправить команду без ожидания ответа.
    /// </summary>
    public async Task<bool> SendCommandNoResponseAsync(
        byte commandId, 
        byte[] payload,
        CancellationToken cancellationToken = default)
    {
        await _commandLock.WaitAsync(cancellationToken);
        try
        {
            var actualPayloadLength = Math.Min(payload.Length, ProtocolConstants.PayloadSize);
            var packet = new ProtocolPacket
            {
                CommandId = commandId,
                PayloadLength = (ushort)actualPayloadLength,
                SequenceNumber = _sequenceNumber++,
                Payload = new byte[ProtocolConstants.PayloadSize]
            };
            
            Array.Copy(payload, 0, packet.Payload, 0, actualPayloadLength);
            
            var packetBytes = packet.ToBytes();
            return await _deviceManager.WriteAsync(packetBytes);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending command 0x{CommandId:X2}", commandId);
            return false;
        }
        finally
        {
            _commandLock.Release();
        }
    }
}
