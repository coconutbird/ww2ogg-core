using System.Numerics;

namespace Ww2Ogg.Core.Internal;

/// <summary>
///     Helper methods from the Vorbis specification
/// </summary>
internal static class VorbisHelpers
{
    /// <summary>
    ///     Integer log base 2 - returns the number of bits needed to represent a value.
    ///     Named to match the Vorbis specification.
    /// </summary>

    // ReSharper disable once InconsistentNaming
    internal static int ILog(uint v)
    {
        return 32 - BitOperations.LeadingZeroCount(v);
    }
}
