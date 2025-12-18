using NVorbis;

namespace Ww2Ogg.Core;

/// <summary>
///     Provides validation for converted Ogg Vorbis audio streams.
/// </summary>
/// <remarks>
///     This class uses NVorbis to decode and validate the output of <see cref="WwiseRiffVorbis.GenerateOgg" />.
///     It can detect issues such as wrong codebook selection, corrupted data, or conversion errors.
/// </remarks>
public static class VorbisValidator
{
    /// <summary>
    ///     Validates an Ogg Vorbis stream by attempting to decode audio samples.
    /// </summary>
    /// <param name="stream">
    ///     A <see cref="MemoryStream" /> containing the Ogg Vorbis data to validate.
    ///     The stream position will be restored after validation.
    /// </param>
    /// <exception cref="CodebookException">
    ///     Thrown when validation fails, indicating the audio cannot be decoded properly.
    ///     This typically means the wrong codebook library was used during conversion.
    /// </exception>
    /// <remarks>
    ///     <para>
    ///         This method reads up to 10 packets of audio data and checks for:
    ///     </para>
    ///     <list type="bullet">
    ///         <item>
    ///             <description>Valid Ogg/Vorbis stream structure</description>
    ///         </item>
    ///         <item>
    ///             <description>Correct CRC checksums</description>
    ///         </item>
    ///         <item>
    ///             <description>Decodable audio samples (no NaN/Infinity values)</description>
    ///         </item>
    ///         <item>
    ///             <description>Reasonable sample values (not clipping excessively)</description>
    ///         </item>
    ///     </list>
    ///     <para>
    ///         If more than 10% of samples in any packet are invalid, validation fails.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code>
    /// using var output = new MemoryStream();
    /// converter.GenerateOgg(output);
    /// VorbisValidator.Validate(output); // Throws if invalid
    /// </code>
    /// </example>
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
