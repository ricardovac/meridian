using Meridian.Application.Abstractions;
using Meridian.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Infrastructure.Persistence.Repositories;

public sealed class AccountRepository : IAccountRepository
{
    private readonly MeridianDbContext _context;

    public AccountRepository(MeridianDbContext context) => _context = context;

    public Task<Account?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.Accounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Account>> ListByOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken = default) =>
        await _context.Accounts
            .Where(a => a.OwnerUserId == ownerUserId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<Account?> FindSystemAccountAsync(string currency, CancellationToken cancellationToken = default)
    {
        var tracked = _context.Accounts.Local
            .FirstOrDefault(a => a.IsSystem && a.Currency == currency);
        if (tracked is not null)
            return Task.FromResult<Account?>(tracked);

        return _context.Accounts.FirstOrDefaultAsync(
            a => a.IsSystem && a.Currency == currency, cancellationToken);
    }

    public void Add(Account account) => _context.Accounts.Add(account);
}
