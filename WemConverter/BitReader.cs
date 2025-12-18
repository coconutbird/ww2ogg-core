namespace WemConverter;

/// <summary>
/// Reads individual bits from a stream (LSB first)
/// </summary>
public class BitReader
{
    private readonly Stream _stream;
    private byte _bitBuffer;
    private int _bitsLeft;
    private long _totalBitsRead;

    public BitReader(Stream stream)
    {
        _stream = stream;
        _bitBuffer = 0;
        _bitsLeft = 0;
        _totalBitsRead = 0;
    }

    public long TotalBitsRead => _totalBitsRead;

    public bool ReadBit()
    {
        if (_bitsLeft == 0)
        {
            int c = _stream.ReadByte();
            if (c < 0)
                throw new EndOfStreamException("Out of bits");
            _bitBuffer = (byte)c;
            _bitsLeft = 8;
        }

        _totalBitsRead++;
        _bitsLeft--;
        return ((_bitBuffer & (0x80 >> _bitsLeft)) != 0);
    }

    public uint ReadBits(int count)
    {
        if (count > 32)
            throw new ArgumentException("Cannot read more than 32 bits at once");

        uint result = 0;
        for (int i = 0; i < count; i++)
        {
            if (ReadBit())
                result |= (1u << i);
        }
        return result;
    }
}

