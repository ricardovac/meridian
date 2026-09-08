using Meridian.Domain.Entities;

namespace Meridian.Application.Abstractions;

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    void Add(User user);
}

public interface IAccountRepository
{
    Task<Account?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Account>> ListByOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken = default);
    Task<Account?> FindSystemAccountAsync(string currency, CancellationToken cancellationToken = default);
    void Add(Account account);
}

public interface ILedgerEntryRepository
{
    void AddRange(IEnumerable<LedgerEntry> entries);
    Task<(IReadOnlyList<LedgerEntry> Items, int Total)> GetPageAsync(
        Guid accountId, int page, int pageSize, CancellationToken cancellationToken = default);
}

public interface ITransferRepository
{
    Task<Transfer?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<Transfer> Items, int Total)> GetPageAsync(
        Guid accountId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<Transfer> Items, int Total)> GetPageByOwnerAsync(
        Guid ownerUserId, int page, int pageSize, CancellationToken cancellationToken = default);
    void Add(Transfer transfer);
}

public interface IOutboxRepository
{
    void Add(OutboxMessage message);
}

public interface IIdempotencyRepository
{
    Task<IdempotencyRecord?> FindAsync(Guid userId, string key, CancellationToken cancellationToken = default);
    void Add(IdempotencyRecord record);
    void Remove(IdempotencyRecord record);
}
