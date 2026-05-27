namespace EReader.Core.Exceptions;

public class AuthorizationException : Exception
{
    public string Code { get; }

    public AuthorizationException(string message) : base(message)
    {
        Code = "FORBIDDEN";
    }
}
