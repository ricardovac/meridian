using Meridian.Application.Exceptions;

namespace Meridian.Application.Common;

public static class ConcurrencyRetry
{
    public const int DefaultMaxAttempts = 3;

    public static async Task<T> ExecuteAsync<T>(
        Func<Task<T>> operation,
        Action? onConflict = null,
        int maxAttempts = DefaultMaxAttempts)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (ConcurrencyConflictException) when (attempt < maxAttempts)
            {
                onConflict?.Invoke();
            }
        }
    }
}
