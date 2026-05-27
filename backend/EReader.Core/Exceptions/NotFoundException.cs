namespace EReader.Core.Exceptions;

public class NotFoundException : Exception
{
    public string Code { get; }

    public NotFoundException(string message, string code = "RESOURCE_NOT_FOUND") : base(message)
    {
        Code = code;
    }
}
