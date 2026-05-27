namespace EReader.Core.Exceptions;

public class AuthenticationException : Exception
{
    public string Code { get; }

    public AuthenticationException(string code, string message) : base(message)
    {
        Code = code;
    }
}
