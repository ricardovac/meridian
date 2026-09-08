using Meridian.Application.Abstractions;
using Meridian.Application.Common;
using Meridian.Application.Dtos;
using Meridian.Application.Exceptions;

namespace Meridian.Application.Services;

public interface ITransferService
{
    Task<TransferDto> CreateAsync(
        Guid userId, Guid sourceAccountId, Guid destinationAccountId, decimal amount,
        string? description, CancellationToken cancellationToken = default);
    Task<TransferDto> GetAsync(Guid userId, Guid transferId, CancellationToken cancellationToken = default);
    Task<PagedResult<TransferDto>> ListAsync(
        Guid userId, Guid accountId, int? page, int? pageSize, CancellationToken cancellationToken = default);
}

public sealed class TransferService : ITransferService
{
    private readonly IAccountRepository _accounts;
    private readonly ITransferRepository _transfers;
    private readonly ILedgerEntryRepository _ledger;
    private readonly IOutboxRepository _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public TransferService(
        IAccountRepository accounts,
        ITransferRepository transfers,
        ILedgerEntryRepository ledger,
        IOutboxRepository outbox,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _accounts = accounts;
        _transfers = transfers;
        _ledger = ledger;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public Task<TransferDto> CreateAsync(
        Guid userId, Guid sourceAccountId, Guid destinationAccountId, decimal amount,
        string? description, CancellationToken cancellationToken = default)
    {
        return ConcurrencyRetry.ExecuteAsync(
            async () =>
            {
                var source = await _accounts.GetAsync(sourceAccountId, cancellationToken);
                if (source is null || source.OwnerUserId != userId)
                    throw new NotFoundException("Source account not found.");

                var destination = await _accounts.GetAsync(destinationAccountId, cancellationToken)
                    ?? throw new NotFoundException("Destination account not found.");

                var result = Domain.Entities.Transfer.Execute(
                    source, destination, amount, description, _clock.UtcNow);

                _transfers.Add(result.Transfer);
                _ledger.AddRange(new[] { result.DebitEntry, result.CreditEntry });
                OutboxWriter.Enqueue(_outbox, result.Event);

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return TransferDto.From(result.Transfer);
            },
            onConflict: _unitOfWork.ClearTracking);
    }

    public async Task<TransferDto> GetAsync(Guid userId, Guid transferId, CancellationToken cancellationToken = default)
    {
        var transfer = await _transfers.GetAsync(transferId, cancellationToken)
            ?? throw new NotFoundException("Transfer not found.");

        var source = await _accounts.GetAsync(transfer.SourceAccountId, cancellationToken);
        var destination = await _accounts.GetAsync(transfer.DestinationAccountId, cancellationToken);
        var involvesCaller = source?.OwnerUserId == userId || destination?.OwnerUserId == userId;
        if (!involvesCaller)
            throw new NotFoundException("Transfer not found.");

        return TransferDto.From(transfer);
    }

    public async Task<PagedResult<TransferDto>> ListAsync(
        Guid userId, Guid accountId, int? page, int? pageSize, CancellationToken cancellationToken = default)
    {
        var account = await _accounts.GetAsync(accountId, cancellationToken);
        if (account is null || account.OwnerUserId != userId)
            throw new NotFoundException("Account not found.");

        var (normalizedPage, normalizedSize) = Paging.Normalize(page, pageSize);
        var (items, total) = await _transfers.GetPageAsync(accountId, normalizedPage, normalizedSize, cancellationToken);
        return new PagedResult<TransferDto>(
            items.Select(TransferDto.From).ToList(), total, normalizedPage, normalizedSize);
    }
}
