namespace MacroKeyboard.Communication.Protocol;

/// <summary>
/// Структура пакета протокола
/// </summary>
public class ProtocolPacket
{
    /// <summary>
    /// Magic byte (0xA5)
    /// </summary>
    public byte Magic { get; set; } = ProtocolConstants.MagicByte;
    
    /// <summary>
    /// ID команды
    /// </summary>
    public byte CommandId { get; set; }
    
    /// <summary>
    /// Длина полезной нагрузки
    /// </summary>
    public ushort PayloadLength { get; set; }
    
    /// <summary>
    /// Номер пакета в последовательности
    /// </summary>
    public ushort SequenceNumber { get; set; }
    
    /// <summary>
    /// Полезная нагрузка (56 байт)
    /// </summary>
    public byte[] Payload { get; set; } = new byte[ProtocolConstants.PayloadSize];
    
    /// <summary>
    /// Контрольная сумма (XOR)
    /// </summary>
    public byte Checksum { get; set; }
    
    /// <summary>
    /// End byte (0x5A)
    /// </summary>
    public byte EndByte { get; set; } = ProtocolConstants.EndByte;
    
    /// <summary>
    /// Конвертировать пакет в массив байтов
    /// </summary>
    public byte[] ToBytes()
    {
        var packet = new byte[ProtocolConstants.PacketSize];
        
        packet[0] = Magic;
        packet[1] = CommandId;
        packet[2] = (byte)(PayloadLength & 0xFF);
        packet[3] = (byte)((PayloadLength >> 8) & 0xFF);
        packet[4] = (byte)(SequenceNumber & 0xFF);
        packet[5] = (byte)((SequenceNumber >> 8) & 0xFF);
        
        Array.Copy(Payload, 0, packet, 6, Math.Min(Payload.Length, ProtocolConstants.PayloadSize));
        
        // Вычислить checksum
        packet[62] = CalculateChecksum(packet, 0, 62);
        packet[63] = EndByte;
        
        return packet;
    }
    
    /// <summary>
    /// Создать пакет из массива байтов
    /// </summary>
    public static ProtocolPacket? FromBytes(byte[] data)
    {
        if (data.Length != ProtocolConstants.PacketSize)
            return null;
        
        // Проверить magic byte
        if (data[0] != ProtocolConstants.MagicByte)
            return null;
        
        // Проверить end byte
        if (data[63] != ProtocolConstants.EndByte)
            return null;
        
        // Проверить checksum
        byte calculatedChecksum = CalculateChecksum(data, 0, 62);
        if (data[62] != calculatedChecksum)
            return null;
        
        var packet = new ProtocolPacket
        {
            Magic = data[0],
            CommandId = data[1],
            PayloadLength = (ushort)(data[2] | (data[3] << 8)),
            SequenceNumber = (ushort)(data[4] | (data[5] << 8)),
            Checksum = data[62],
            EndByte = data[63]
        };
        
        Array.Copy(data, 6, packet.Payload, 0, ProtocolConstants.PayloadSize);
        
        return packet;
    }
    
    /// <summary>
    /// Вычислить контрольную сумму (XOR)
    /// </summary>
    private static byte CalculateChecksum(byte[] data, int start, int length)
    {
        byte checksum = 0;
        for (int i = start; i < start + length; i++)
        {
            checksum ^= data[i];
        }
        return checksum;
    }
    
    /// <summary>
    /// Проверить валидность пакета
    /// </summary>
    public bool IsValid()
    {
        return Magic == ProtocolConstants.MagicByte && 
               EndByte == ProtocolConstants.EndByte;
    }
}
