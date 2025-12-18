namespace WemConverter;

public class WemException : Exception
{
    public WemException(string message) : base(message) { }
    public WemException(string message, Exception inner) : base(message, inner) { }
}

public class FileOpenException : WemException
{
    public FileOpenException(string fileName)
        : base($"Error opening {fileName}")
    {
        FileName = fileName;
    }

    public string FileName { get; }
}

public class ParseException : WemException
{
    public ParseException(string message) : base($"Parse error: {message}") { }
}

public class SizeMismatchException : CodebookException
{
    public SizeMismatchException(long expected, long actual)
        : base($"Parse error: expected {expected} bytes, read {actual} - likely wrong codebook")
    {
        ExpectedSize = expected;
        ActualSize = actual;
    }

    public long ExpectedSize { get; }
    public long ActualSize { get; }
}

public class CodebookException : WemException
{
    public CodebookException(string message) : base(message) { }
}

public class InvalidCodebookIdException : CodebookException
{
    public InvalidCodebookIdException(int id)
        : base($"Parse error: invalid codebook id {id}, try --inline-codebooks")
    {
        CodebookId = id;
    }

    public int CodebookId { get; }
}
