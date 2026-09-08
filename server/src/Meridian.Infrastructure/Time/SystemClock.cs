using Meridian.Application.Abstractions;

namespace Meridian.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
