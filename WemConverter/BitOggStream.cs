namespace WemConverter;

/// <summary>
/// Writes bits to an Ogg stream with proper page formatting
/// </summary>
public class BitOggStream : IDisposable
{
    private const int HeaderBytes = 27;
    private const int MaxSegments = 255;
    private const int SegmentSize = 255;

    private readonly Stream _outputStream;
    private byte _bitBuffer;
    private int _bitsStored;
    private int _payloadBytes;
    private bool _first;
    private bool _continued;
    private readonly byte[] _pageBuffer;
    private ulong _granule;
    private uint _seqNo;

    public BitOggStream(Stream outputStream)
    {
        _outputStream = outputStream;
        _bitBuffer = 0;
        _bitsStored = 0;
        _payloadBytes = 0;
        _first = true;
        _continued = false;
        _pageBuffer = new byte[HeaderBytes + MaxSegments + SegmentSize * MaxSegments];
        _granule = 0;
        _seqNo = 0;
    }

    public void SetGranule(ulong granule)
    {
        _granule = granule;
    }

    public void WriteBit(bool bit)
    {
        if (bit)
            _bitBuffer |= (byte)(1 << _bitsStored);

        _bitsStored++;
        if (_bitsStored == 8)
        {
            FlushBits();
        }
    }

    public void WriteBits(uint value, int count)
    {
        for (int i = 0; i < count; i++)
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
            int segments = (_payloadBytes + SegmentSize) / SegmentSize;
            if (segments == MaxSegments + 1) segments = MaxSegments;

            // Move payload back
            for (int i = 0; i < _payloadBytes; i++)
            {
                _pageBuffer[HeaderBytes + segments + i] = _pageBuffer[HeaderBytes + MaxSegments + i];
            }

            // OggS header
            _pageBuffer[0] = (byte)'O';
            _pageBuffer[1] = (byte)'g';
            _pageBuffer[2] = (byte)'g';
            _pageBuffer[3] = (byte)'S';
            _pageBuffer[4] = 0; // stream_structure_version
            _pageBuffer[5] = (byte)((_continued ? 1 : 0) | (_first ? 2 : 0) | (last ? 4 : 0));
            
            // Granule position (64-bit)
            WriteUInt32LE(_pageBuffer, 6, (uint)(_granule & 0xFFFFFFFF));
            WriteUInt32LE(_pageBuffer, 10, (uint)(_granule >> 32));
            
            // Stream serial number
            WriteUInt32LE(_pageBuffer, 14, 1);
            
            // Page sequence number
            WriteUInt32LE(_pageBuffer, 18, _seqNo);
            
            // Checksum (0 for now, will be computed)
            WriteUInt32LE(_pageBuffer, 22, 0);
            
            // Segment count
            _pageBuffer[26] = (byte)segments;

            // Lacing values
            int bytesLeft = _payloadBytes;
            for (int i = 0; i < segments; i++)
            {
                if (bytesLeft >= SegmentSize)
                {
                    bytesLeft -= SegmentSize;
                    _pageBuffer[27 + i] = SegmentSize;
                }
                else
                {
                    _pageBuffer[27 + i] = (byte)bytesLeft;
                }
            }

            // Compute and write checksum
            int totalSize = HeaderBytes + segments + _payloadBytes;
            uint checksum = OggCrc.Compute(_pageBuffer, totalSize);
            WriteUInt32LE(_pageBuffer, 22, checksum);

            // Write to output
            _outputStream.Write(_pageBuffer, 0, totalSize);

            _seqNo++;
            _first = false;
            _continued = nextContinued;
            _payloadBytes = 0;
        }
    }

    private static void WriteUInt32LE(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    public void Dispose()
    {
        FlushPage();
    }
}

