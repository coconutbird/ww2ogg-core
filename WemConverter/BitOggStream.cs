namespace WemConverter;

/// <summary>
///     Writes bits to an Ogg stream with proper page formatting
/// </summary>
public class BitOggStream(Stream outputStream) : IDisposable
{
    private const int HeaderBytes = 27;
    private const int MaxSegments = 255;
    private const int SegmentSize = 255;
    private readonly byte[] _pageBuffer = new byte[HeaderBytes + MaxSegments + SegmentSize * MaxSegments];

    private byte _bitBuffer;
    private int _bitsStored;
    private bool _continued;
    private bool _first = true;
    private ulong _granule;
    private int _payloadBytes;
    private uint _seqNo;

    public void Dispose()
    {
        FlushPage();
    }

    public void SetGranule(ulong granule)
    {
        _granule = granule;
    }

    public void WriteBit(bool bit)
    {
        if (bit) _bitBuffer |= (byte) (1 << _bitsStored);

        _bitsStored++;

        if (_bitsStored == 8)
        {
            FlushBits();
        }
    }

    public void WriteBits(uint value, int count)
    {
        for (var i = 0; i < count; i++)
        {
            WriteBit((value & (1u << i)) != 0);
        }
    }

    public void FlushBits()
    {
        if (_bitsStored != 0)
        {
            if (_payloadBytes == SegmentSize * MaxSegments)
            {
                throw new ParseException("ran out of space in an Ogg packet");
            }

            _pageBuffer[HeaderBytes + MaxSegments + _payloadBytes] = _bitBuffer;
            _payloadBytes++;

            _bitsStored = 0;
            _bitBuffer = 0;
        }
    }

    public void FlushPage(bool nextContinued = false, bool last = false)
    {
        if (_payloadBytes != SegmentSize * MaxSegments)
        {
            FlushBits();
        }

        if (_payloadBytes != 0)
        {
            var segments = (_payloadBytes + SegmentSize) / SegmentSize;
            if (segments == MaxSegments + 1) segments = MaxSegments;

            // Move payload back
            for (var i = 0; i < _payloadBytes; i++)
            {
                _pageBuffer[HeaderBytes + segments + i] = _pageBuffer[HeaderBytes + MaxSegments + i];
            }

            // OggS header
            _pageBuffer[0] = (byte) 'O';
            _pageBuffer[1] = (byte) 'g';
            _pageBuffer[2] = (byte) 'g';
            _pageBuffer[3] = (byte) 'S';
            _pageBuffer[4] = 0; // stream_structure_version
            _pageBuffer[5] = (byte) ((_continued ? 1 : 0) | (_first ? 2 : 0) | (last ? 4 : 0));

            // Granule position (64-bit)
            WriteUint64Le(_pageBuffer, 6, _granule);

            // Stream serial number
            WriteUInt32Le(_pageBuffer, 14, 1);

            // Page sequence number
            WriteUInt32Le(_pageBuffer, 18, _seqNo);

            // Checksum (0 for now, will be computed)
            WriteUInt32Le(_pageBuffer, 22, 0);

            // Segment count
            _pageBuffer[26] = (byte) segments;

            // Lacing values
            var bytesLeft = _payloadBytes;

            for (var i = 0; i < segments; i++)
            {
                if (bytesLeft >= SegmentSize)
                {
                    bytesLeft -= SegmentSize;
                    _pageBuffer[27 + i] = SegmentSize;
                }
                else
                {
                    _pageBuffer[27 + i] = (byte) bytesLeft;
                }
            }

            // Compute and write checksum
            var totalSize = HeaderBytes + segments + _payloadBytes;
            var checksum = OggCrc.Compute(_pageBuffer, totalSize);
            WriteUInt32Le(_pageBuffer, 22, checksum);

            // Write to output
            outputStream.Write(_pageBuffer, 0, totalSize);

            _seqNo++;
            _first = false;
            _continued = nextContinued;
            _payloadBytes = 0;
        }
    }

    private static void WriteUInt32Le(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte) (value & 0xFF);
        buffer[offset + 1] = (byte) ((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte) ((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte) ((value >> 24) & 0xFF);
    }

    private static void WriteUint64Le(byte[] buffer, int offset, ulong value)
    {
        WriteUInt32Le(buffer, offset, (uint) (value & 0xFFFFFFFF));
        WriteUInt32Le(buffer, offset + 4, (uint) (value >> 32));
    }
}
