namespace Ww2Ogg.Core;

/// <summary>
///     Base exception for all Wwise audio conversion errors.
/// </summary>
public class WemException(string message) : Exception(message);

/// <summary>
///     Exception thrown when a required file cannot be opened or found.
/// </summary>
public class FileOpenException(string fileName) : WemException($"Error opening {fileName}")
{
    /// <summary>
    ///     Gets the name of the file that could not be opened.
    /// </summary>
    public string FileName { get; } = fileName;
}

/// <summary>
///     Exception thrown when the input file contains invalid or malformed data.
/// </summary>
public class ParseException(string message) : WemException($"Parse error: {message}");

/// <summary>
///     Exception thrown when the codebook data size doesn't match the expected size.
///     This typically indicates the wrong codebook library is being used.
/// </summary>
public class SizeMismatchException(long expected, long actual) : CodebookException(
    $"Parse error: expected {expected} bytes, read {actual} - likely wrong codebook")
{
    /// <summary>
    ///     Gets the expected size in bytes.
    /// </summary>
    public long ExpectedSize { get; } = expected;

    /// <summary>
    ///     Gets the actual size that was read.
    /// </summary>
    public long ActualSize { get; } = actual;
}

/// <summary>
///     Exception thrown when there is an error with codebook data or decoding.
///     This typically indicates the wrong codebook library is being used for the audio file.
/// </summary>
public class CodebookException(string message) : WemException(message);

/// <summary>
///     Exception thrown when a codebook ID referenced in the audio file is not found in the codebook library.
/// </summary>
/// <remarks>
///     This usually means the file requires inline codebooks (use <c>inlineCodebooks: true</c>)
///     or a different codebook library.
/// </remarks>
public class InvalidCodebookIdException(int id)
    : CodebookException($"Parse error: invalid codebook id {id}, try --inline-codebooks")
{
    /// <summary>
    ///     Gets the invalid codebook ID that was not found.
    /// </summary>
    public int CodebookId { get; } = id;
}
