using Meridian.Application.Abstractions;
using Meridian.Domain.Entities;

namespace Meridian.Application.Services;

public interface ISystemAccountProvider
{
    Task<Account> GetOrCreateAsync(string currency, CancellationToken cancellationToken = default);
}

public sealed class SystemAccountProvider : ISystemAccountProvider
{
    private readonly IAccountRepository _accounts;
    private readonly IClock _clock;

    public SystemAccountProvider(IAccountRepository accounts, IClock clock)
    {
        _accounts = accounts;
        _clock = clock;
    }

    public async Task<Account> GetOrCreateAsync(string currency, CancellationToken cancellationToken = default)
    {
        var existing = await _accounts.FindSystemAccountAsync(currency, cancellationToken);
        if (existing is not null)
            return existing;

        var system = Account.CreateSystem(currency, _clock.UtcNow);
        _accounts.Add(system);
        return system;
    }
}
