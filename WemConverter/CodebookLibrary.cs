using System.Collections.Concurrent;
using System.Reflection;

namespace WemConverter;

/// <summary>
///     Loads and rebuilds Vorbis codebooks from packed binary data
/// </summary>
public class CodebookLibrary
{
    private static readonly ConcurrentDictionary<string, CodebookLibrary> Cache = new();
    private readonly int _codebookCount;

    private readonly byte[]? _codebookData;
    private readonly int[]? _codebookOffsets;

    /// <summary>
    ///     Creates an empty codebook library (for inline codebooks)
    /// </summary>
    public CodebookLibrary()
    {
        _codebookData = null;
        _codebookOffsets = null;
        _codebookCount = 0;
    }

    /// <summary>
    ///     Loads codebook library from a file
    /// </summary>
    public CodebookLibrary(string filename)
    {
        if (!File.Exists(filename))
        {
            throw new FileOpenException(filename);
        }

        var fileData = File.ReadAllBytes(filename);
        LoadFromBytes(fileData, out _codebookData, out _codebookOffsets, out _codebookCount);
    }

    private CodebookLibrary(byte[] fileData)
    {
        LoadFromBytes(fileData, out _codebookData, out _codebookOffsets, out _codebookCount);
    }

    /// <summary>
    ///     Loads codebook library from embedded resource (cached)
    /// </summary>
    public static CodebookLibrary FromEmbeddedResource(string resourceName)
    {
        return Cache.GetOrAdd(
            resourceName,
            name =>
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream(name)
                                   ?? throw new FileOpenException(name);

                using var ms = new MemoryStream();
                stream.CopyTo(ms);

                return new CodebookLibrary(ms.ToArray());
            });
    }

    public ReadOnlySpan<byte> GetCodebook(int index)
    {
        if (_codebookData == null || _codebookOffsets == null)
        {
            throw new ParseException("codebook library not loaded");
        }

        if (index < 0 || index >= _codebookCount - 1)
        {
            throw new InvalidCodebookIdException(index);
        }

        var start = _codebookOffsets[index];
        var length = _codebookOffsets[index + 1] - start;

        return new ReadOnlySpan<byte>(_codebookData, start, length);
    }

    public int GetCodebookSize(int index)
    {
        if (_codebookData == null || _codebookOffsets == null)
        {
            throw new ParseException("codebook library not loaded");
        }

        if (index < 0 || index >= _codebookCount - 1)
        {
            return -1;
        }

        return _codebookOffsets[index + 1] - _codebookOffsets[index];
    }

    /// <summary>
    ///     Rebuild codebook from library by index
    /// </summary>
    public void Rebuild(int index, BitOggStream output)
    {
        var codebook = GetCodebook(index);
        using var ms = new MemoryStream(codebook.ToArray());
        var reader = new BitReader(ms);
        Rebuild(reader, (uint) codebook.Length, output);
    }

    /// <summary>
    ///     Copy codebook directly (for inline/full setup)
    /// </summary>
    public void Copy(BitReader input, BitOggStream output)
    {
        // IN: 24 bit identifier, 16 bit dimensions, 24 bit entry count
        var id = input.ReadBits(24);
        var dimensions = input.ReadBits(16);
        var entries = input.ReadBits(24);

        if (id != 0x564342) // "BCV"
        {
            throw new ParseException("invalid codebook identifier");
        }

        // OUT: same
        output.WriteBits(id, 24);
        output.WriteBits(dimensions, 16);
        output.WriteBits(entries, 24);

        CopyCodebookData(input, output, entries, dimensions);
    }

    /// <summary>
    ///     Rebuild codebook from stripped format
    /// </summary>
    public void Rebuild(BitReader input, uint codebookSize, BitOggStream output)
    {
        // IN: 4 bit dimensions, 14 bit entry count
        var dimensions = input.ReadBits(4);
        var entries = input.ReadBits(14);

        // OUT: 24 bit identifier, 16 bit dimensions, 24 bit entry count
        output.WriteBits(0x564342, 24); // "BCV"
        output.WriteBits(dimensions, 16);
        output.WriteBits(entries, 24);

        RebuildCodebookData(input, output, entries, dimensions, codebookSize);
    }

    private static void LoadFromBytes(
        byte[] fileData,
        out byte[] codebookData,
        out int[] codebookOffsets,
        out int codebookCount)
    {
        var fileSize = fileData.Length;
        var offsetOffset = BitConverter.ToInt32(fileData, fileSize - 4);
        codebookCount = (fileSize - offsetOffset) / 4;

        codebookData = new byte[offsetOffset];
        Array.Copy(fileData, 0, codebookData, 0, offsetOffset);

        codebookOffsets = new int[codebookCount];

        for (var i = 0; i < codebookCount; i++)
        {
            codebookOffsets[i] = BitConverter.ToInt32(fileData, offsetOffset + i * 4);
        }
    }

    // Helper methods will be added via str-replace-editor
    private static int ILog(uint v)
    {
        var ret = 0;

        while (v != 0)
        {
            ret++;
            v >>= 1;
        }

        return ret;
    }

    private static uint BookMapType1Quantvals(uint entries, uint dimensions)
    {
        var bits = ILog(entries);
        var vals = entries >> ((bits - 1) * ((int) dimensions - 1) / (int) dimensions);

        while (true)
        {
            ulong acc = 1;
            ulong acc1 = 1;

            for (uint i = 0; i < dimensions; i++)
            {
                acc *= vals;
                acc1 *= vals + 1;
            }

            if (acc <= entries && acc1 > entries)
            {
                return vals;
            }

            if (acc > entries)
            {
                vals--;
            }
            else
            {
                vals++;
            }
        }
    }

    private void CopyCodebookData(BitReader input, BitOggStream output, uint entries, uint dimensions)
    {
        // IN/OUT: 1 bit ordered flag
        var ordered = input.ReadBits(1);
        output.WriteBits(ordered, 1);

        if (ordered != 0)
        {
            // IN/OUT: 5 bit initial length
            var initialLength = input.ReadBits(5);
            output.WriteBits(initialLength, 5);

            uint currentEntry = 0;

            while (currentEntry < entries)
            {
                var numBits = ILog(entries - currentEntry);
                var number = input.ReadBits(numBits);
                output.WriteBits(number, numBits);
                currentEntry += number;
            }

            if (currentEntry > entries)
            {
                throw new ParseException("current_entry out of range");
            }
        }
        else
        {
            // IN/OUT: 1 bit sparse flag
            var sparse = input.ReadBits(1);
            output.WriteBits(sparse, 1);

            for (uint i = 0; i < entries; i++)
            {
                var presentBool = true;

                if (sparse != 0)
                {
                    var present = input.ReadBits(1);
                    output.WriteBits(present, 1);
                    presentBool = present != 0;
                }

                if (presentBool)
                {
                    var codewordLength = input.ReadBits(5);
                    output.WriteBits(codewordLength, 5);
                }
            }
        }

        // Lookup table
        var lookupType = input.ReadBits(4);
        output.WriteBits(lookupType, 4);

        if (lookupType == 1)
        {
            var min = input.ReadBits(32);
            var max = input.ReadBits(32);
            var valueLength = input.ReadBits(4);
            var sequenceFlag = input.ReadBits(1);
            output.WriteBits(min, 32);
            output.WriteBits(max, 32);
            output.WriteBits(valueLength, 4);
            output.WriteBits(sequenceFlag, 1);

            var quantvals = BookMapType1Quantvals(entries, dimensions);

            for (uint i = 0; i < quantvals; i++)
            {
                var val = input.ReadBits((int) (valueLength + 1));
                output.WriteBits(val, (int) (valueLength + 1));
            }
        }
        else if (lookupType == 2)
        {
            throw new ParseException("didn't expect lookup type 2");
        }
        else if (lookupType != 0)
        {
            throw new ParseException("invalid lookup type");
        }
    }

    private void RebuildCodebookData(
        BitReader input,
        BitOggStream output,
        uint entries,
        uint dimensions,
        uint codebookSize)
    {
        // IN/OUT: 1 bit ordered flag
        var ordered = input.ReadBits(1);
        output.WriteBits(ordered, 1);

        if (ordered != 0)
        {
            var initialLength = input.ReadBits(5);
            output.WriteBits(initialLength, 5);

            uint currentEntry = 0;

            while (currentEntry < entries)
            {
                var numBits = ILog(entries - currentEntry);
                var number = input.ReadBits(numBits);
                output.WriteBits(number, numBits);
                currentEntry += number;
            }

            if (currentEntry > entries)
            {
                throw new ParseException("current_entry out of range");
            }
        }
        else
        {
            // IN: 3 bit codeword length length, 1 bit sparse flag
            var codewordLengthLength = input.ReadBits(3);
            var sparse = input.ReadBits(1);

            if (codewordLengthLength == 0 || codewordLengthLength > 5)
            {
                throw new ParseException("nonsense codeword length");
            }

            // OUT: 1 bit sparse flag
            output.WriteBits(sparse, 1);

            for (uint i = 0; i < entries; i++)
            {
                var presentBool = true;

                if (sparse != 0)
                {
                    var present = input.ReadBits(1);
                    output.WriteBits(present, 1);
                    presentBool = present != 0;
                }

                if (presentBool)
                {
                    // IN: n bit codeword length-1
                    var codewordLength = input.ReadBits((int) codewordLengthLength);

                    // OUT: 5 bit codeword length-1
                    output.WriteBits(codewordLength, 5);
                }
            }
        }

        // Lookup table
        // IN: 1 bit lookup type
        var lookupType = input.ReadBits(1);

        // OUT: 4 bit lookup type
        output.WriteBits(lookupType, 4);

        if (lookupType == 1)
        {
            var min = input.ReadBits(32);
            var max = input.ReadBits(32);
            var valueLength = input.ReadBits(4);
            var sequenceFlag = input.ReadBits(1);
            output.WriteBits(min, 32);
            output.WriteBits(max, 32);
            output.WriteBits(valueLength, 4);
            output.WriteBits(sequenceFlag, 1);

            var quantvals = BookMapType1Quantvals(entries, dimensions);

            for (uint i = 0; i < quantvals; i++)
            {
                var val = input.ReadBits((int) (valueLength + 1));
                output.WriteBits(val, (int) (valueLength + 1));
            }
        }
        else if (lookupType != 0)
        {
            throw new ParseException("invalid lookup type");
        }

        // Check size if specified
        if (codebookSize != 0 && input.TotalBitsRead / 8 + 1 != codebookSize)
        {
            throw new SizeMismatchException(codebookSize, input.TotalBitsRead / 8 + 1);
        }
    }
}
