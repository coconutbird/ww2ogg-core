using NVorbis;

namespace WemConverter;

/// <summary>
///     Validates Ogg Vorbis output by decoding with NVorbis
/// </summary>
public static class VorbisValidator
{
    /// <summary>
    ///     Validates the Ogg Vorbis output by attempting to decode audio.
    ///     NVorbis will throw if structure, CRCs, or codebooks are invalid.
    /// </summary>
    public static void Validate(MemoryStream stream)
    {
        var lastPosition = stream.Position;

        stream.Position = 0;

        try
        {
            using var vorbis = new VorbisReader(stream, false);

            var buffer = new float[4096];
            var totalSamples = 0;
            var reads = 0;
            const int maxReads = 10;

            while (reads < maxReads)
            {
                var samplesRead = vorbis.ReadSamples(buffer, 0, buffer.Length);

                if (samplesRead == 0) break;

                totalSamples += samplesRead;
                reads++;

                var badSamples = 0;

                for (var i = 0; i < samplesRead; i++)
                {
                    if (float.IsNaN(buffer[i]) || float.IsInfinity(buffer[i])) badSamples++;
                    if (Math.Abs(buffer[i]) > 10.0f) badSamples++;
                }

                if (badSamples > samplesRead / 10)
                {
                    throw new CodebookException(
                        $"Decoded audio has {badSamples}/{samplesRead} invalid samples - likely wrong codebook");
                }
            }

            if (totalSamples == 0)
            {
                throw new CodebookException("No audio samples could be decoded - likely wrong codebook");
            }
        }
        catch (InvalidDataException ex)
        {
            throw new CodebookException($"Vorbis decode failed: {ex.Message} - likely wrong codebook");
        }
        catch (EndOfStreamException ex)
        {
            throw new CodebookException($"Vorbis decode failed (unexpected end): {ex.Message} - likely wrong codebook");
        }
        finally
        {
            stream.Position = lastPosition;
        }
    }
}
