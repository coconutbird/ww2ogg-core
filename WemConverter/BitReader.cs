namespace WemConverter;

/// <summary>
///     Reads individual bits (LSB first) from memory or a stream
/// </summary>
public class BitReader
{
    private readonly ReadOnlyMemory<byte> _memory;
    private readonly Stream? _stream;
    private int _bitPos;
    private int _bytePos;
    private byte _currentByte;

    public BitReader(ReadOnlyMemory<byte> data)
    {
        _memory = data;
        _stream = null;
        _bytePos = 0;
        _bitPos = 0;
    }

    public BitReader(Stream stream)
    {
        _memory = ReadOnlyMemory<byte>.Empty;
        _stream = stream;
        _bytePos = 0;
        _bitPos = 0;
    }

    public long TotalBitsRead => _bytePos * 8L + _bitPos;

    public bool ReadBit()
    {
        if (_bitPos == 0)
        {
            if (_stream == null)
            {
                var span = _memory.Span;

                if (_bytePos >= span.Length)
                {
                    throw new EndOfStreamException("Out of bits");
                }

                _currentByte = span[_bytePos];
            }
            else
            {
                var b = _stream.ReadByte();

                if (b < 0)
                {
                    throw new EndOfStreamException("Out of bits");
                }

                _currentByte = (byte) b;
            }
        }

        var bit = (_currentByte & (1 << _bitPos)) != 0;
        _bitPos++;

        if (_bitPos == 8)
        {
            _bitPos = 0;
            _bytePos++;
        }

        return bit;
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
