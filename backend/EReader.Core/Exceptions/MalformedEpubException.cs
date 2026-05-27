namespace EReader.Core.Exceptions;

public class MalformedEpubException : Exception
{
    public string Code { get; } = "EPUB_MALFORMED";

    public MalformedEpubException(string message) : base(message) { }

    public MalformedEpubException(string message, Exception inner) : base(message, inner) { }
}
