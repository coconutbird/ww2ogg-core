namespace WemConverter;

public enum ForcePacketFormat
{
    NoForce,
    ForceModPackets,
    ForceNoModPackets
}

/// <summary>
/// Converts Wwise RIFF/RIFX Vorbis to standard Ogg Vorbis
/// </summary>
public class WwiseRiffVorbis
{
    private const string Version = "0.24";
    private static readonly byte[] VorbisBytes = "vorbis"u8.ToArray();

    private readonly Stream _inputStream;
    private readonly long _fileSize;
    private readonly bool _littleEndian;

    // Chunk offsets and sizes
    private long _riffSize;
    private long _fmtOffset = -1, _cueOffset = -1, _listOffset = -1;
    private long _smplOffset = -1, _vorbOffset = -1, _dataOffset = -1;
    private long _fmtSize = -1, _cueSize = -1, _listSize = -1;
    private long _smplSize = -1, _vorbSize = -1, _dataSize = -1;

    // RIFF fmt
    private ushort _channels;
    private uint _sampleRate;
    private uint _avgBytesPerSecond;
    private ushort _extUnk;
    private uint _subtype;

    // Cue info
    private uint _cueCount;

    // Smpl info
    private uint _loopCount, _loopStart, _loopEnd;

    // Vorbis info
    private uint _sampleCount;
    private uint _setupPacketOffset;
    private uint _firstAudioPacketOffset;
    private uint _uid;
    private byte _blocksize0Pow;
    private byte _blocksize1Pow;

    // Flags
    private readonly bool _inlineCodebooks;
    private readonly bool _fullSetup;
    private bool _headerTriadPresent;
    private bool _oldPacketHeaders;
    private bool _noGranule;
    private bool _modPackets;

    private readonly CodebookLibrary _codebooks;

    public WwiseRiffVorbis(
        Stream inputStream,
        CodebookLibrary codebooks,
        bool inlineCodebooks = false,
        bool fullSetup = false,
        ForcePacketFormat forcePacketFormat = ForcePacketFormat.NoForce)
    {
        _inputStream = inputStream;
        _codebooks = codebooks;
        _inlineCodebooks = inlineCodebooks;
        _fullSetup = fullSetup;

        _inputStream.Seek(0, SeekOrigin.End);
        _fileSize = _inputStream.Position;
        _inputStream.Seek(0, SeekOrigin.Begin);

        // Check RIFF header
        var riffHead = new byte[4];
        var waveHead = new byte[4];
        _inputStream.Read(riffHead, 0, 4);

        if (riffHead.AsSpan().SequenceEqual("RIFX"u8))
        {
            _littleEndian = false;
        }
        else if (riffHead.AsSpan().SequenceEqual("RIFF"u8))
        {
            _littleEndian = true;
        }
        else
        {
            throw new ParseException("missing RIFF");
        }

        _riffSize = ReadUInt32() + 8;
        if (_riffSize > _fileSize)
            throw new ParseException("RIFF truncated");

        _inputStream.Read(waveHead, 0, 4);
        if (!waveHead.AsSpan().SequenceEqual("WAVE"u8))
            throw new ParseException("missing WAVE");

        // Read chunks
        ReadChunks();

        // Validate and parse chunks
        ParseFmtChunk();
        ParseCueChunk();
        ParseSmplChunk();
        ParseVorbChunk(forcePacketFormat);
        ValidateLoops();
    }

    private uint ReadUInt32()
    {
        var buffer = new byte[4];
        _inputStream.Read(buffer, 0, 4);
        return _littleEndian
            ? BitConverter.ToUInt32(buffer, 0)
            : (uint)((buffer[0] << 24) | (buffer[1] << 16) | (buffer[2] << 8) | buffer[3]);
    }

    private ushort ReadUInt16()
    {
        var buffer = new byte[2];
        _inputStream.Read(buffer, 0, 2);
        return _littleEndian
            ? BitConverter.ToUInt16(buffer, 0)
            : (ushort)((buffer[0] << 8) | buffer[1]);
    }

    private byte ReadByte()
    {
        int b = _inputStream.ReadByte();
        if (b < 0) throw new EndOfStreamException();
        return (byte)b;
    }

    private void ReadChunks()
    {
        long chunkOffset = 12;
        while (chunkOffset < _riffSize)
        {
            _inputStream.Seek(chunkOffset, SeekOrigin.Begin);
            if (chunkOffset + 8 > _riffSize)
                throw new ParseException("chunk header truncated");

            var chunkType = new byte[4];
            _inputStream.Read(chunkType, 0, 4);
            uint chunkSize = ReadUInt32();

            if (chunkType.AsSpan().SequenceEqual("fmt "u8))
            {
                _fmtOffset = chunkOffset + 8;
                _fmtSize = chunkSize;
            }
            else if (chunkType.AsSpan().SequenceEqual("cue "u8))
            {
                _cueOffset = chunkOffset + 8;
                _cueSize = chunkSize;
            }
            else if (chunkType.AsSpan().SequenceEqual("LIST"u8))
            {
                _listOffset = chunkOffset + 8;
                _listSize = chunkSize;
            }
            else if (chunkType.AsSpan().SequenceEqual("smpl"u8))
            {
                _smplOffset = chunkOffset + 8;
                _smplSize = chunkSize;
            }
            else if (chunkType.AsSpan().SequenceEqual("vorb"u8))
            {
                _vorbOffset = chunkOffset + 8;
                _vorbSize = chunkSize;
            }
            else if (chunkType.AsSpan().SequenceEqual("data"u8))
            {
                _dataOffset = chunkOffset + 8;
                _dataSize = chunkSize;
            }

            chunkOffset = chunkOffset + 8 + chunkSize;
        }

        if (chunkOffset > _riffSize)
            throw new ParseException("chunk truncated");

        if (_fmtOffset == -1 || _dataOffset == -1)
            throw new ParseException("expected fmt, data chunks");
    }

    private void ParseFmtChunk()
    {
        if (_vorbOffset == -1 && _fmtSize != 0x42)
            throw new ParseException("expected 0x42 fmt if vorb missing");

        if (_vorbOffset != -1 && _fmtSize != 0x28 && _fmtSize != 0x18 && _fmtSize != 0x12)
            throw new ParseException("bad fmt size");

        if (_vorbOffset == -1 && _fmtSize == 0x42)
        {
            // Fake vorb offset
            _vorbOffset = _fmtOffset + 0x18;
        }

        _inputStream.Seek(_fmtOffset, SeekOrigin.Begin);
        if (ReadUInt16() != 0xFFFF)
            throw new ParseException("bad codec id");

        _channels = ReadUInt16();
        _sampleRate = ReadUInt32();
        _avgBytesPerSecond = ReadUInt32();

        if (ReadUInt16() != 0)
            throw new ParseException("bad block align");
        if (ReadUInt16() != 0)
            throw new ParseException("expected 0 bps");
        if (ReadUInt16() != _fmtSize - 0x12)
            throw new ParseException("bad extra fmt length");

        if (_fmtSize - 0x12 >= 2)
        {
            _extUnk = ReadUInt16();
            if (_fmtSize - 0x12 >= 6)
            {
                _subtype = ReadUInt32();
            }
        }

        if (_fmtSize == 0x28)
        {
            var whoknowsbuf = new byte[16];
            byte[] expected = [1, 0, 0, 0, 0, 0, 0x10, 0, 0x80, 0, 0, 0xAA, 0, 0x38, 0x9b, 0x71];
            _inputStream.Read(whoknowsbuf, 0, 16);
            if (!whoknowsbuf.AsSpan().SequenceEqual(expected))
                throw new ParseException("expected signature in extra fmt?");
        }
    }

    private void ParseCueChunk()
    {
        if (_cueOffset != -1)
        {
            _inputStream.Seek(_cueOffset, SeekOrigin.Begin);
            _cueCount = ReadUInt32();
        }
    }

    private void ParseSmplChunk()
    {
        if (_smplOffset != -1)
        {
            _inputStream.Seek(_smplOffset + 0x1C, SeekOrigin.Begin);
            _loopCount = ReadUInt32();

            if (_loopCount != 1)
                throw new ParseException("expected one loop");

            _inputStream.Seek(_smplOffset + 0x2C, SeekOrigin.Begin);
            _loopStart = ReadUInt32();
            _loopEnd = ReadUInt32();
        }
    }

    private void ParseVorbChunk(ForcePacketFormat forcePacketFormat)
    {
        switch (_vorbSize)
        {
            case -1:
            case 0x28:
            case 0x2A:
            case 0x2C:
            case 0x32:
            case 0x34:
                _inputStream.Seek(_vorbOffset, SeekOrigin.Begin);
                break;
            default:
                throw new ParseException("bad vorb size");
        }

        _sampleCount = ReadUInt32();

        switch (_vorbSize)
        {
            case -1:
            case 0x2A:
                _noGranule = true;
                _inputStream.Seek(_vorbOffset + 0x4, SeekOrigin.Begin);
                uint modSignal = ReadUInt32();

                if (modSignal != 0x4A && modSignal != 0x4B && modSignal != 0x69 && modSignal != 0x70)
                {
                    _modPackets = true;
                }
                _inputStream.Seek(_vorbOffset + 0x10, SeekOrigin.Begin);
                break;
            default:
                _inputStream.Seek(_vorbOffset + 0x18, SeekOrigin.Begin);
                break;
        }

        if (forcePacketFormat == ForcePacketFormat.ForceNoModPackets)
            _modPackets = false;
        else if (forcePacketFormat == ForcePacketFormat.ForceModPackets)
            _modPackets = true;

        _setupPacketOffset = ReadUInt32();
        _firstAudioPacketOffset = ReadUInt32();

        switch (_vorbSize)
        {
            case -1:
            case 0x2A:
                _inputStream.Seek(_vorbOffset + 0x24, SeekOrigin.Begin);
                break;
            case 0x32:
            case 0x34:
                _inputStream.Seek(_vorbOffset + 0x2C, SeekOrigin.Begin);
                break;
        }

        switch (_vorbSize)
        {
            case 0x28:
            case 0x2C:
                _headerTriadPresent = true;
                _oldPacketHeaders = true;
                break;
            case -1:
            case 0x2A:
            case 0x32:
            case 0x34:
                _uid = ReadUInt32();
                _blocksize0Pow = ReadByte();
                _blocksize1Pow = ReadByte();
                break;
        }
    }

    private void ValidateLoops()
    {
        if (_loopCount != 0)
        {
            if (_loopEnd == 0)
                _loopEnd = _sampleCount;
            else
                _loopEnd = _loopEnd + 1;

            if (_loopStart >= _sampleCount || _loopEnd > _sampleCount || _loopStart > _loopEnd)
                throw new ParseException("loops out of range");
        }
    }

    public void GenerateOgg(Stream outputStream)
    {
        using var ogg = new BitOggStream(outputStream);
        bool[]? modeBlockflag = null;
        int modeBits = 0;
        bool prevBlockflag = false;

        if (_headerTriadPresent)
        {
            GenerateOggHeaderWithTriad(ogg);
        }
        else
        {
            GenerateOggHeader(ogg, out modeBlockflag, out modeBits);
        }

        // For granule calculation
        uint blocksize0 = 1u << _blocksize0Pow;
        uint blocksize1 = 1u << _blocksize1Pow;
        long granulePos = 0;
        uint prevBlocksize = 0;
        bool firstPacket = true;

        // Audio pages
        long offset = _dataOffset + _firstAudioPacketOffset;
        while (offset < _dataOffset + _dataSize)
        {
            uint size;
            uint granule;
            long packetHeaderSize;
            long packetPayloadOffset;
            long nextOffset;

            if (_oldPacketHeaders)
            {
                var packet = new Packet8(_inputStream, offset, _littleEndian);
                packetHeaderSize = packet.HeaderSize;
                size = packet.Size;
                packetPayloadOffset = packet.Offset;
                granule = packet.Granule;
                nextOffset = packet.NextOffset;
            }
            else
            {
                var packet = new Packet(_inputStream, offset, _littleEndian, _noGranule);
                packetHeaderSize = packet.HeaderSize;
                size = packet.Size;
                packetPayloadOffset = packet.Offset;
                granule = packet.Granule;
                nextOffset = packet.NextOffset;
            }

            if (offset + packetHeaderSize > _dataOffset + _dataSize)
                throw new ParseException("page header truncated");

            offset = packetPayloadOffset;
            _inputStream.Seek(offset, SeekOrigin.Begin);

            // Determine granule for this page
            bool isLastPacket = nextOffset >= _dataOffset + _dataSize;

            if (_noGranule)
            {
                // Calculate granule from block sizes
                // First, peek at the mode number to determine block size
                uint currBlocksize;
                if (modeBlockflag != null && modeBits > 0 && size > 0)
                {
                    // Read mode number from first byte
                    int firstByte = _inputStream.ReadByte();
                    _inputStream.Seek(offset, SeekOrigin.Begin); // Seek back

                    uint modeNumber;
                    if (_modPackets)
                    {
                        // Mode number is in the first modeBits bits
                        modeNumber = (uint)(firstByte & ((1 << modeBits) - 1));
                    }
                    else
                    {
                        // Standard Vorbis: skip packet type bit (always 0 for audio)
                        modeNumber = (uint)((firstByte >> 1) & ((1 << modeBits) - 1));
                    }

                    if (modeNumber < modeBlockflag.Length)
                    {
                        currBlocksize = modeBlockflag[modeNumber] ? blocksize1 : blocksize0;
                    }
                    else
                    {
                        currBlocksize = blocksize0; // Fallback
                    }
                }
                else
                {
                    currBlocksize = blocksize0; // Fallback
                }

                // Calculate samples for this packet
                if (firstPacket)
                {
                    // First packet produces no audio (priming)
                    firstPacket = false;
                }
                else
                {
                    // Samples = (prev_blocksize + curr_blocksize) / 4
                    granulePos += (prevBlocksize + currBlocksize) / 4;
                }

                prevBlocksize = currBlocksize;

                // Use calculated granule, but for last packet use sample_count if available
                if (isLastPacket && _sampleCount > 0)
                {
                    ogg.SetGranule(_sampleCount);
                }
                else
                {
                    ogg.SetGranule((ulong)granulePos);
                }
            }
            else
            {
                // Use granule from packet
                ogg.SetGranule(granule == 0xFFFFFFFF ? 1 : granule);
            }

            // First byte handling
            if (_modPackets)
            {
                if (modeBlockflag == null)
                    throw new ParseException("didn't load mode_blockflag");

                // OUT: 1 bit packet type (0 == audio)
                ogg.WriteBits(0, 1);

                _inputStream.Seek(offset, SeekOrigin.Begin);
                var bitReader = new BitReader(_inputStream);

                // IN/OUT: N bit mode number
                uint modeNumber = bitReader.ReadBits(modeBits);
                ogg.WriteBits(modeNumber, modeBits);

                // IN: remaining bits of first byte
                uint remainder = bitReader.ReadBits(8 - modeBits);

                if (modeBlockflag[modeNumber])
                {
                    // Long window, peek at next frame
                    bool nextBlockflag = false;
                    if (nextOffset + packetHeaderSize <= _dataOffset + _dataSize)
                    {
                        var nextPacket = new Packet(_inputStream, nextOffset, _littleEndian, _noGranule);
                        if (nextPacket.Size > 0)
                        {
                            _inputStream.Seek(nextPacket.Offset, SeekOrigin.Begin);
                            var nextBitReader = new BitReader(_inputStream);
                            uint nextModeNumber = nextBitReader.ReadBits(modeBits);
                            nextBlockflag = modeBlockflag[nextModeNumber];
                        }
                    }

                    // OUT: previous/next window type bits
                    ogg.WriteBits(prevBlockflag ? 1u : 0u, 1);
                    ogg.WriteBits(nextBlockflag ? 1u : 0u, 1);

                    _inputStream.Seek(offset + 1, SeekOrigin.Begin);
                }

                prevBlockflag = modeBlockflag[modeNumber];
                ogg.WriteBits(remainder, 8 - modeBits);
            }
            else
            {
                int v = _inputStream.ReadByte();
                if (v < 0) throw new ParseException("file truncated");
                ogg.WriteBits((uint)v, 8);
            }

            // Remainder of packet
            for (uint i = 1; i < size; i++)
            {
                int v = _inputStream.ReadByte();
                if (v < 0) throw new ParseException("file truncated");
                ogg.WriteBits((uint)v, 8);
            }

            offset = nextOffset;
            ogg.FlushPage(false, offset == _dataOffset + _dataSize);
        }

        if (offset > _dataOffset + _dataSize)
            throw new ParseException("page truncated");
    }

    private static int ILog(uint v)
    {
        int ret = 0;
        while (v != 0)
        {
            ret++;
            v >>= 1;
        }
        return ret;
    }

    private void GenerateOggHeader(BitOggStream ogg, out bool[] modeBlockflag, out int modeBits)
    {
        // Generate identification packet
        WriteVorbisPacketHeader(ogg, 1);
        ogg.WriteBits(0, 32); // version
        ogg.WriteBits(_channels, 8);
        ogg.WriteBits(_sampleRate, 32);
        ogg.WriteBits(0, 32); // bitrate_max
        ogg.WriteBits(_avgBytesPerSecond * 8, 32); // bitrate_nominal
        ogg.WriteBits(0, 32); // bitrate_minimum
        ogg.WriteBits(_blocksize0Pow, 4);
        ogg.WriteBits(_blocksize1Pow, 4);
        ogg.WriteBits(1, 1); // framing
        ogg.FlushPage();

        // Generate comment packet
        WriteVorbisPacketHeader(ogg, 3);
        string vendor = $"converted from Audiokinetic Wwise by ww2ogg {Version}";
        ogg.WriteBits((uint)vendor.Length, 32);
        foreach (char c in vendor)
            ogg.WriteBits((uint)c, 8);

        if (_loopCount == 0)
        {
            ogg.WriteBits(0, 32); // no user comments
        }
        else
        {
            ogg.WriteBits(2, 32); // two comments
            string loopStart = $"LoopStart={_loopStart}";
            string loopEnd = $"LoopEnd={_loopEnd}";

            ogg.WriteBits((uint)loopStart.Length, 32);
            foreach (char c in loopStart)
                ogg.WriteBits((uint)c, 8);

            ogg.WriteBits((uint)loopEnd.Length, 32);
            foreach (char c in loopEnd)
                ogg.WriteBits((uint)c, 8);
        }

        ogg.WriteBits(1, 1); // framing
        ogg.FlushPage();

        // Generate setup packet
        WriteVorbisPacketHeader(ogg, 5);

        var setupPacket = new Packet(_inputStream, _dataOffset + _setupPacketOffset, _littleEndian, _noGranule);
        _inputStream.Seek(setupPacket.Offset, SeekOrigin.Begin);
        if (setupPacket.Granule != 0)
            throw new ParseException("setup packet granule != 0");

        var setupReader = new BitReader(_inputStream);

        // Codebook count
        uint codebookCountLess1 = setupReader.ReadBits(8);
        uint codebookCount = codebookCountLess1 + 1;
        ogg.WriteBits(codebookCountLess1, 8);

        // Rebuild codebooks
        if (_inlineCodebooks)
        {
            for (uint i = 0; i < codebookCount; i++)
            {
                if (_fullSetup)
                    _codebooks.Copy(setupReader, ogg);
                else
                    _codebooks.Rebuild(setupReader, 0, ogg);
            }
        }
        else
        {
            for (uint i = 0; i < codebookCount; i++)
            {
                uint codebookId = setupReader.ReadBits(10);
                try
                {
                    _codebooks.Rebuild((int)codebookId, ogg);
                }
                catch (InvalidCodebookIdException)
                {
                    if (codebookId == 0x342)
                    {
                        uint codebookIdentifier = setupReader.ReadBits(14);
                        if (codebookIdentifier == 0x1590)
                            throw new ParseException("invalid codebook id 0x342, try --full-setup");
                    }
                    throw;
                }
            }
        }

        // Time domain transforms placeholder
        ogg.WriteBits(0, 6); // time_count_less1
        ogg.WriteBits(0, 16); // dummy_time_value

        if (_fullSetup)
        {
            // Full setup - just copy remaining bits, no mode info needed
            while (setupReader.TotalBitsRead < setupPacket.Size * 8u)
            {
                ogg.WriteBits(setupReader.ReadBits(1), 1);
            }
            // Full setup doesn't use mod packets, so these won't be used
            modeBlockflag = Array.Empty<bool>();
            modeBits = 0;
        }
        else
        {
            // Parse and rebuild floor, residue, mapping, mode data
            RebuildSetupData(setupReader, ogg, codebookCount, out modeBlockflag, out modeBits);
        }

        ogg.FlushPage();

        if ((setupReader.TotalBitsRead + 7) / 8 != setupPacket.Size)
            throw new ParseException("didn't read exactly setup packet");

        if (setupPacket.NextOffset != _dataOffset + _firstAudioPacketOffset)
            throw new ParseException("first audio packet doesn't follow setup packet");

        return;
    }

    private void WriteVorbisPacketHeader(BitOggStream ogg, byte type)
    {
        ogg.WriteBits(type, 8);
        foreach (byte b in VorbisBytes)
            ogg.WriteBits(b, 8);
    }

    private void RebuildSetupData(BitReader reader, BitOggStream ogg, uint codebookCount,
        out bool[] modeBlockflag, out int modeBits)
    {
        // Floor count
        uint floorCountLess1 = reader.ReadBits(6);
        uint floorCount = floorCountLess1 + 1;
        ogg.WriteBits(floorCountLess1, 6);

        for (uint i = 0; i < floorCount; i++)
        {
            ogg.WriteBits(1, 16); // floor type 1
            RebuildFloor(reader, ogg, codebookCount);
        }

        // Residue count
        uint residueCountLess1 = reader.ReadBits(6);
        uint residueCount = residueCountLess1 + 1;
        ogg.WriteBits(residueCountLess1, 6);

        for (uint i = 0; i < residueCount; i++)
        {
            RebuildResidue(reader, ogg, codebookCount);
        }

        // Mapping count
        uint mappingCountLess1 = reader.ReadBits(6);
        uint mappingCount = mappingCountLess1 + 1;
        ogg.WriteBits(mappingCountLess1, 6);

        for (uint i = 0; i < mappingCount; i++)
        {
            RebuildMapping(reader, ogg, floorCount, residueCount);
        }

        // Mode count
        uint modeCountLess1 = reader.ReadBits(6);
        uint modeCount = modeCountLess1 + 1;
        ogg.WriteBits(modeCountLess1, 6);

        modeBlockflag = new bool[modeCount];
        modeBits = ILog(modeCount - 1);

        for (uint i = 0; i < modeCount; i++)
        {
            uint blockFlag = reader.ReadBits(1);
            ogg.WriteBits(blockFlag, 1);
            modeBlockflag[i] = blockFlag != 0;

            ogg.WriteBits(0, 16); // windowtype
            ogg.WriteBits(0, 16); // transformtype

            uint mapping = reader.ReadBits(8);
            ogg.WriteBits(mapping, 8);
            if (mapping >= mappingCount)
                throw new ParseException("invalid mode mapping");
        }

        ogg.WriteBits(1, 1); // framing
    }

    private void RebuildFloor(BitReader reader, BitOggStream ogg, uint codebookCount)
    {
        uint floor1Partitions = reader.ReadBits(5);
        ogg.WriteBits(floor1Partitions, 5);

        var floor1PartitionClassList = new uint[floor1Partitions];
        uint maximumClass = 0;

        for (uint j = 0; j < floor1Partitions; j++)
        {
            uint floor1PartitionClass = reader.ReadBits(4);
            ogg.WriteBits(floor1PartitionClass, 4);
            floor1PartitionClassList[j] = floor1PartitionClass;
            if (floor1PartitionClass > maximumClass)
                maximumClass = floor1PartitionClass;
        }

        var floor1ClassDimensionsList = new uint[maximumClass + 1];

        for (uint j = 0; j <= maximumClass; j++)
        {
            uint classDimensionsLess1 = reader.ReadBits(3);
            ogg.WriteBits(classDimensionsLess1, 3);
            floor1ClassDimensionsList[j] = classDimensionsLess1 + 1;

            uint classSubclasses = reader.ReadBits(2);
            ogg.WriteBits(classSubclasses, 2);

            if (classSubclasses != 0)
            {
                uint masterbook = reader.ReadBits(8);
                ogg.WriteBits(masterbook, 8);
                if (masterbook >= codebookCount)
                    throw new ParseException("invalid floor1 masterbook");
            }

            for (uint k = 0; k < (1u << (int)classSubclasses); k++)
            {
                uint subclassBookPlus1 = reader.ReadBits(8);
                ogg.WriteBits(subclassBookPlus1, 8);
                int subclassBook = (int)subclassBookPlus1 - 1;
                if (subclassBook >= 0 && (uint)subclassBook >= codebookCount)
                    throw new ParseException("invalid floor1 subclass book");
            }
        }

        uint floor1MultiplierLess1 = reader.ReadBits(2);
        ogg.WriteBits(floor1MultiplierLess1, 2);

        uint rangebits = reader.ReadBits(4);
        ogg.WriteBits(rangebits, 4);

        for (uint j = 0; j < floor1Partitions; j++)
        {
            uint currentClassNumber = floor1PartitionClassList[j];
            for (uint k = 0; k < floor1ClassDimensionsList[currentClassNumber]; k++)
            {
                uint x = reader.ReadBits((int)rangebits);
                ogg.WriteBits(x, (int)rangebits);
            }
        }
    }

    private void RebuildResidue(BitReader reader, BitOggStream ogg, uint codebookCount)
    {
        uint residueType = reader.ReadBits(2);
        ogg.WriteBits(residueType, 16);
        if (residueType > 2)
            throw new ParseException("invalid residue type");

        uint residueBegin = reader.ReadBits(24);
        uint residueEnd = reader.ReadBits(24);
        uint residuePartitionSizeLess1 = reader.ReadBits(24);
        uint residueClassificationsLess1 = reader.ReadBits(6);
        uint residueClassbook = reader.ReadBits(8);

        uint residueClassifications = residueClassificationsLess1 + 1;

        ogg.WriteBits(residueBegin, 24);
        ogg.WriteBits(residueEnd, 24);
        ogg.WriteBits(residuePartitionSizeLess1, 24);
        ogg.WriteBits(residueClassificationsLess1, 6);
        ogg.WriteBits(residueClassbook, 8);

        if (residueClassbook >= codebookCount)
            throw new ParseException("invalid residue classbook");

        var residueCascade = new uint[residueClassifications];

        for (uint j = 0; j < residueClassifications; j++)
        {
            uint lowBits = reader.ReadBits(3);
            ogg.WriteBits(lowBits, 3);

            uint bitflag = reader.ReadBits(1);
            ogg.WriteBits(bitflag, 1);

            uint highBits = 0;
            if (bitflag != 0)
            {
                highBits = reader.ReadBits(5);
                ogg.WriteBits(highBits, 5);
            }

            residueCascade[j] = highBits * 8 + lowBits;
        }

        for (uint j = 0; j < residueClassifications; j++)
        {
            for (int k = 0; k < 8; k++)
            {
                if ((residueCascade[j] & (1 << k)) != 0)
                {
                    uint residueBook = reader.ReadBits(8);
                    ogg.WriteBits(residueBook, 8);
                    if (residueBook >= codebookCount)
                        throw new ParseException("invalid residue book");
                }
            }
        }
    }

    private void RebuildMapping(BitReader reader, BitOggStream ogg, uint floorCount, uint residueCount)
    {
        ogg.WriteBits(0, 16); // mapping type 0

        uint submapsFlag = reader.ReadBits(1);
        ogg.WriteBits(submapsFlag, 1);

        uint submaps = 1;
        if (submapsFlag != 0)
        {
            uint submapsLess1 = reader.ReadBits(4);
            submaps = submapsLess1 + 1;
            ogg.WriteBits(submapsLess1, 4);
        }

        uint squarePolarFlag = reader.ReadBits(1);
        ogg.WriteBits(squarePolarFlag, 1);

        if (squarePolarFlag != 0)
        {
            uint couplingStepsLess1 = reader.ReadBits(8);
            uint couplingSteps = couplingStepsLess1 + 1;
            ogg.WriteBits(couplingStepsLess1, 8);

            int couplingBits = ILog((uint)(_channels - 1));
            for (uint j = 0; j < couplingSteps; j++)
            {
                uint magnitude = reader.ReadBits(couplingBits);
                uint angle = reader.ReadBits(couplingBits);
                ogg.WriteBits(magnitude, couplingBits);
                ogg.WriteBits(angle, couplingBits);

                if (angle == magnitude || magnitude >= _channels || angle >= _channels)
                    throw new ParseException("invalid coupling");
            }
        }

        uint mappingReserved = reader.ReadBits(2);
        ogg.WriteBits(mappingReserved, 2);
        if (mappingReserved != 0)
            throw new ParseException("mapping reserved field nonzero");

        if (submaps > 1)
        {
            for (uint j = 0; j < _channels; j++)
            {
                uint mappingMux = reader.ReadBits(4);
                ogg.WriteBits(mappingMux, 4);
                if (mappingMux >= submaps)
                    throw new ParseException("mapping_mux >= submaps");
            }
        }

        for (uint j = 0; j < submaps; j++)
        {
            uint timeConfig = reader.ReadBits(8);
            ogg.WriteBits(timeConfig, 8);

            uint floorNumber = reader.ReadBits(8);
            ogg.WriteBits(floorNumber, 8);
            if (floorNumber >= floorCount)
                throw new ParseException("invalid floor mapping");

            uint residueNumber = reader.ReadBits(8);
            ogg.WriteBits(residueNumber, 8);
            if (residueNumber >= residueCount)
                throw new ParseException("invalid residue mapping");
        }
    }

    private void GenerateOggHeaderWithTriad(BitOggStream ogg)
    {
        long offset = _dataOffset + _setupPacketOffset;

        // Copy identification packet
        var infoPacket = new Packet8(_inputStream, offset, _littleEndian);
        if (infoPacket.Granule != 0)
            throw new ParseException("information packet granule != 0");

        _inputStream.Seek(infoPacket.Offset, SeekOrigin.Begin);
        int packetType = _inputStream.ReadByte();
        if (packetType != 1)
            throw new ParseException("wrong type for information packet");

        ogg.WriteBits((uint)packetType, 8);
        for (uint i = 1; i < infoPacket.Size; i++)
        {
            int b = _inputStream.ReadByte();
            ogg.WriteBits((uint)b, 8);
        }
        ogg.FlushPage();
        offset = infoPacket.NextOffset;

        // Copy comment packet
        var commentPacket = new Packet8(_inputStream, offset, _littleEndian);
        if (commentPacket.Granule != 0)
            throw new ParseException("comment packet granule != 0");

        _inputStream.Seek(commentPacket.Offset, SeekOrigin.Begin);
        packetType = _inputStream.ReadByte();
        if (packetType != 3)
            throw new ParseException("wrong type for comment packet");

        ogg.WriteBits((uint)packetType, 8);
        for (uint i = 1; i < commentPacket.Size; i++)
        {
            int b = _inputStream.ReadByte();
            ogg.WriteBits((uint)b, 8);
        }
        ogg.FlushPage();
        offset = commentPacket.NextOffset;

        // Copy setup packet
        var setupPacket = new Packet8(_inputStream, offset, _littleEndian);
        if (setupPacket.Granule != 0)
            throw new ParseException("setup packet granule != 0");

        _inputStream.Seek(setupPacket.Offset, SeekOrigin.Begin);
        var setupReader = new BitReader(_inputStream);

        uint type = setupReader.ReadBits(8);
        if (type != 5)
            throw new ParseException("wrong type for setup packet");
        ogg.WriteBits(type, 8);

        // 'vorbis'
        for (int i = 0; i < 6; i++)
        {
            uint c = setupReader.ReadBits(8);
            ogg.WriteBits(c, 8);
        }

        // Codebook count
        uint codebookCountLess1 = setupReader.ReadBits(8);
        uint codebookCount = codebookCountLess1 + 1;
        ogg.WriteBits(codebookCountLess1, 8);

        var cbl = new CodebookLibrary();
        for (uint i = 0; i < codebookCount; i++)
        {
            cbl.Copy(setupReader, ogg);
        }

        while (setupReader.TotalBitsRead < setupPacket.Size * 8u)
        {
            ogg.WriteBits(setupReader.ReadBits(1), 1);
        }

        ogg.FlushPage();

        if (setupPacket.NextOffset != _dataOffset + _firstAudioPacketOffset)
            throw new ParseException("first audio packet doesn't follow setup packet");
    }
}

/// <summary>
/// Modern 2 or 6 byte packet header
/// </summary>
internal class Packet
{
    public long HeaderSize { get; }
    public long Offset { get; }
    public uint Size { get; }
    public uint Granule { get; }
    public long NextOffset { get; }

    public Packet(Stream stream, long offset, bool littleEndian, bool noGranule = false)
    {
        stream.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[6];

        if (noGranule)
        {
            stream.Read(buffer, 0, 2);
            Size = littleEndian
                ? BitConverter.ToUInt16(buffer, 0)
                : (ushort)((buffer[0] << 8) | buffer[1]);
            Granule = 0;
            HeaderSize = 2;
        }
        else
        {
            stream.Read(buffer, 0, 6);
            Size = littleEndian
                ? BitConverter.ToUInt16(buffer, 0)
                : (ushort)((buffer[0] << 8) | buffer[1]);
            Granule = littleEndian
                ? BitConverter.ToUInt32(buffer, 2)
                : (uint)((buffer[2] << 24) | (buffer[3] << 16) | (buffer[4] << 8) | buffer[5]);
            HeaderSize = 6;
        }

        Offset = offset + HeaderSize;
        NextOffset = offset + HeaderSize + Size;
    }
}

/// <summary>
/// Old 8 byte packet header
/// </summary>
internal class Packet8
{
    public long HeaderSize => 8;
    public long Offset { get; }
    public uint Size { get; }
    public uint Granule { get; }
    public long NextOffset { get; }

    public Packet8(Stream stream, long offset, bool littleEndian)
    {
        stream.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[8];
        stream.Read(buffer, 0, 8);

        if (littleEndian)
        {
            Size = BitConverter.ToUInt32(buffer, 0);
            Granule = BitConverter.ToUInt32(buffer, 4);
        }
        else
        {
            Size = (uint)((buffer[0] << 24) | (buffer[1] << 16) | (buffer[2] << 8) | buffer[3]);
            Granule = (uint)((buffer[4] << 24) | (buffer[5] << 16) | (buffer[6] << 8) | buffer[7]);
        }

        Offset = offset + HeaderSize;
        NextOffset = offset + HeaderSize + Size;
    }
}

