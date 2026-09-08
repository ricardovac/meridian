using Meridian.Application.Abstractions;
using Meridian.Application.Common;
using Meridian.Application.Dtos;
using Meridian.Application.Exceptions;
using Meridian.Domain.Entities;

namespace Meridian.Application.Services;

public interface IAccountService
{
    Task<AccountDto> CreateAsync(Guid userId, string name, string currency, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AccountDto>> ListAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<AccountDto> GetAsync(Guid userId, Guid accountId, CancellationToken cancellationToken = default);
    Task<PagedResult<LedgerEntryDto>> GetEntriesAsync(
        Guid userId, Guid accountId, int? page, int? pageSize, CancellationToken cancellationToken = default);
    Task<TransferDto> DepositAsync(Guid userId, Guid accountId, decimal amount, CancellationToken cancellationToken = default);
}

public sealed class AccountService : IAccountService
{
    private readonly IAccountRepository _accounts;
    private readonly ITransferRepository _transfers;
    private readonly ILedgerEntryRepository _ledger;
    private readonly IOutboxRepository _outbox;
    private readonly ISystemAccountProvider _systemAccounts;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public AccountService(
        IAccountRepository accounts,
        ITransferRepository transfers,
        ILedgerEntryRepository ledger,
        IOutboxRepository outbox,
        ISystemAccountProvider systemAccounts,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _accounts = accounts;
        _transfers = transfers;
        _ledger = ledger;
        _outbox = outbox;
        _systemAccounts = systemAccounts;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<AccountDto> CreateAsync(Guid userId, string name, string currency, CancellationToken cancellationToken = default)
    {
        var account = Account.CreateForUser(userId, name, currency, _clock.UtcNow);
        _accounts.Add(account);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return AccountDto.From(account);
    }

    public async Task<IReadOnlyList<AccountDto>> ListAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var accounts = await _accounts.ListByOwnerAsync(userId, cancellationToken);
        return accounts.Select(AccountDto.From).ToList();
    }

    public async Task<AccountDto> GetAsync(Guid userId, Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = await GetOwnedAccountAsync(userId, accountId, cancellationToken);
        return AccountDto.From(account);
    }

    public async Task<PagedResult<LedgerEntryDto>> GetEntriesAsync(
        Guid userId, Guid accountId, int? page, int? pageSize, CancellationToken cancellationToken = default)
    {
        await GetOwnedAccountAsync(userId, accountId, cancellationToken);
        var (normalizedPage, normalizedSize) = Paging.Normalize(page, pageSize);
        var (items, total) = await _ledger.GetPageAsync(accountId, normalizedPage, normalizedSize, cancellationToken);
        return new PagedResult<LedgerEntryDto>(
            items.Select(LedgerEntryDto.From).ToList(), total, normalizedPage, normalizedSize);
    }

    public Task<TransferDto> DepositAsync(Guid userId, Guid accountId, decimal amount, CancellationToken cancellationToken = default)
    {
        return ConcurrencyRetry.ExecuteAsync(
            async () =>
            {
                var account = await GetOwnedAccountAsync(userId, accountId, cancellationToken);
                var systemAccount = await _systemAccounts.GetOrCreateAsync(account.Currency, cancellationToken);

                var result = Domain.Entities.Transfer.Execute(
                    systemAccount, account, amount, "Deposit", _clock.UtcNow);

                _transfers.Add(result.Transfer);
                _ledger.AddRange(new[] { result.DebitEntry, result.CreditEntry });
                OutboxWriter.Enqueue(_outbox, result.Event);

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return TransferDto.From(result.Transfer);
            },
            onConflict: _unitOfWork.ClearTracking,
            maxAttempts: SystemAccountProvider.ContentionMaxAttempts);
    }

    private async Task<Account> GetOwnedAccountAsync(Guid userId, Guid accountId, CancellationToken cancellationToken)
    {
        var account = await _accounts.GetAsync(accountId, cancellationToken);
        if (account is null || account.OwnerUserId != userId)
            throw new NotFoundException("Account not found.");
        return account;
    }
}
