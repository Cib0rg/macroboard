namespace MacroKeyboard.Core.Utilities;

/// <summary>
/// Вычисление CRC32 для проверки целостности данных
/// </summary>
public static class Crc32
{
    private static readonly uint[] Table = GenerateTable();
    
    private static uint[] GenerateTable()
    {
        const uint polynomial = 0xEDB88320;
        var table = new uint[256];
        
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 8; j > 0; j--)
            {
                if ((crc & 1) == 1)
                    crc = (crc >> 1) ^ polynomial;
                else
                    crc >>= 1;
            }
            table[i] = crc;
        }
        
        return table;
    }
    
    /// <summary>
    /// Вычислить CRC32 для массива байтов
    /// </summary>
    public static uint Calculate(byte[] data)
    {
        return Calculate(data, 0, data.Length);
    }
    
    /// <summary>
    /// Вычислить CRC32 для части массива байтов
    /// </summary>
    public static uint Calculate(byte[] data, int offset, int length)
    {
        uint crc = 0xFFFFFFFF;
        
        for (int i = offset; i < offset + length; i++)
        {
            byte index = (byte)(crc ^ data[i]);
            crc = (crc >> 8) ^ Table[index];
        }
        
        return ~crc;
    }
}
