namespace EReader.Core.Exceptions;

public class ConflictException : Exception
{
    public string Code { get; }

    public ConflictException(string message, string code = "RESOURCE_CONFLICT") : base(message)
    {
        Code = code;
    }
}
