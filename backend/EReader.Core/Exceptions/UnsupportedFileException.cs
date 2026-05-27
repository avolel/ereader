namespace EReader.Core.Exceptions;

public class UnsupportedFileException : Exception
{
    public string Code { get; } = "UNSUPPORTED_MEDIA_TYPE";

    public UnsupportedFileException(string message) : base(message) { }
}
