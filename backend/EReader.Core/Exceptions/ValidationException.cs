namespace EReader.Core.Exceptions;

public class ValidationException : Exception
{
    public IReadOnlyDictionary<string, string[]>? Details { get; }

    public ValidationException(string message, IReadOnlyDictionary<string, string[]>? details = null)
        : base(message)
    {
        Details = details;
    }
}
