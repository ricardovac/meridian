namespace Meridian.Application.Abstractions;

public interface IClock
{
    DateTime UtcNow { get; }
}
