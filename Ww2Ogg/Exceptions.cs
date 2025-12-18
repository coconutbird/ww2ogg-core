namespace Ww2Ogg;

public class WemException(string message) : Exception(message);

public class FileOpenException(string fileName) : WemException($"Error opening {fileName}")
{
    public string FileName { get; } = fileName;
}

public class ParseException(string message) : WemException($"Parse error: {message}");

public class SizeMismatchException(long expected, long actual) : CodebookException(
    $"Parse error: expected {expected} bytes, read {actual} - likely wrong codebook")
{
    public long ExpectedSize { get; } = expected;
    public long ActualSize { get; } = actual;
}

public class CodebookException(string message) : WemException(message);

public class InvalidCodebookIdException(int id)
    : CodebookException($"Parse error: invalid codebook id {id}, try --inline-codebooks")
{
    public int CodebookId { get; } = id;
}
