namespace Meridian.Application.Exceptions;

public sealed class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

public sealed class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}

public sealed class RequestValidationException : Exception
{
    public RequestValidationException(string message) : base(message) { }
}

public sealed class InvalidCredentialsException : Exception
{
    public InvalidCredentialsException() : base("Invalid email or password.") { }
}

public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(Exception? innerException = null)
        : base("The resource was modified concurrently.", innerException)
    {
    }
}
