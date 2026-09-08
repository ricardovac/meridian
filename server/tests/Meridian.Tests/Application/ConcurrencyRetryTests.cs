using Meridian.Application.Common;
using Meridian.Application.Exceptions;

namespace Meridian.Tests.Application;

public class ConcurrencyRetryTests
{
    [Fact]
    public async Task ExecuteAsync_RetriesOnConflict_AndSucceeds()
    {
        var attempts = 0;
        var conflicts = 0;

        var result = await ConcurrencyRetry.ExecuteAsync(
            () =>
            {
                attempts++;
                if (attempts < 3)
                    throw new ConcurrencyConflictException();
                return Task.FromResult(42);
            },
            onConflict: () => conflicts++);

        Assert.Equal(42, result);
        Assert.Equal(3, attempts);
        Assert.Equal(2, conflicts);
    }

    [Fact]
    public async Task ExecuteAsync_GivesUp_AfterMaxAttempts()
    {
        var attempts = 0;

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            ConcurrencyRetry.ExecuteAsync<int>(() =>
            {
                attempts++;
                throw new ConcurrencyConflictException();
            }));

        Assert.Equal(ConcurrencyRetry.DefaultMaxAttempts, attempts);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotRetry_OtherExceptions()
    {
        var attempts = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ConcurrencyRetry.ExecuteAsync<int>(() =>
            {
                attempts++;
                throw new InvalidOperationException();
            }));

        Assert.Equal(1, attempts);
    }
}
