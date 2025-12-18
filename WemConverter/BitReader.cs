namespace WemConverter;

/// <summary>
///     Reads individual bits from a stream (LSB first)
/// </summary>
public class BitReader
{
    private readonly Stream _stream;
    private byte _bitBuffer;
    private int _bitsLeft;

    public BitReader(Stream stream)
    {
        _stream = stream;
        _bitBuffer = 0;
        _bitsLeft = 0;
        TotalBitsRead = 0;
    }

    public long TotalBitsRead { get; private set; }

    public bool ReadBit()
    {
        if (_bitsLeft == 0)
        {
            var c = _stream.ReadByte();

            if (c < 0)
            {
                throw new EndOfStreamException("Out of bits");
            }

            _bitBuffer = (byte) c;
            _bitsLeft = 8;
        }

        TotalBitsRead++;
        _bitsLeft--;

        return (_bitBuffer & (0x80 >> _bitsLeft)) != 0;
    }

    public uint ReadBits(int count)
    {
        if (count > 32)
        {
            throw new ArgumentException("Cannot read more than 32 bits at once");
        }

        uint result = 0;

        for (var i = 0; i < count; i++)
        {
            if (ReadBit())
            {
                result |= 1u << i;
            }
        }

        return result;
    }
}
