using Meridian.Application.Abstractions;
using Meridian.Application.Exceptions;
using Meridian.Domain.Entities;

namespace Meridian.Application.Services;

public interface ISystemAccountProvider
{
    Task<Account> GetOrCreateAsync(string currency, CancellationToken cancellationToken = default);
}

public sealed class SystemAccountProvider : ISystemAccountProvider
{
    // A brand-new currency's system account is created lazily on first use, so its first
    // few concurrent callers all race for both the create and the immediate debit of the
    // same row; the default ConcurrencyRetry budget is tuned for steady-state contention,
    // not this one-time cold-start spike, so callers of GetOrCreateAsync retry more.
    public const int ContentionMaxAttempts = 8;

    private readonly IAccountRepository _accounts;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public SystemAccountProvider(IAccountRepository accounts, IUnitOfWork unitOfWork, IClock clock)
    {
        _accounts = accounts;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Account> GetOrCreateAsync(string currency, CancellationToken cancellationToken = default)
    {
        var existing = await _accounts.FindSystemAccountAsync(currency, cancellationToken);
        if (existing is not null)
            return existing;

        var system = Account.CreateSystem(currency, _clock.UtcNow);
        _accounts.Add(system);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return system;
        }
        catch (ConflictException ex)
        {
            _unitOfWork.ClearTracking();
            throw new ConcurrencyConflictException(ex);
        }
    }
}
