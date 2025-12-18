namespace WemConverter;

public class WemException : Exception
{
    public WemException(string message) : base(message) { }
    public WemException(string message, Exception inner) : base(message, inner) { }
}

public class FileOpenException : WemException
{
    public string FileName { get; }
    
    public FileOpenException(string fileName) 
        : base($"Error opening {fileName}")
    {
        FileName = fileName;
    }
}

public class ParseException : WemException
{
    public ParseException(string message) : base($"Parse error: {message}") { }
}

public class SizeMismatchException : ParseException
{
    public long ExpectedSize { get; }
    public long ActualSize { get; }
    
    public SizeMismatchException(long expected, long actual) 
        : base($"expected {expected} bits, read {actual}")
    {
        ExpectedSize = expected;
        ActualSize = actual;
    }
}

public class InvalidCodebookIdException : ParseException
{
    public int CodebookId { get; }
    
    public InvalidCodebookIdException(int id) 
        : base($"invalid codebook id {id}, try --inline-codebooks")
    {
        CodebookId = id;
    }
}

