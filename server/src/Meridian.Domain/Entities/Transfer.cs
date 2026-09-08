using Meridian.Domain.Events;
using Meridian.Domain.Exceptions;

namespace Meridian.Domain.Entities;

public class Transfer
{
    public Guid Id { get; private set; }
    public Guid SourceAccountId { get; private set; }
    public Guid DestinationAccountId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public string? Description { get; private set; }
    public TransferStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private Transfer() { }

    public static TransferResult Execute(
        Account source,
        Account destination,
        decimal amount,
        string? description,
        DateTime occurredAt)
    {
        if (amount <= 0m)
            throw new DomainValidationException("Transfer amount must be greater than zero.");

        if (amount != decimal.Round(amount, 2))
            throw new InvalidAmountScaleException(amount);

        if (source.Id == destination.Id)
            throw new DomainValidationException("Source and destination accounts must differ.");

        if (source.Currency != destination.Currency)
            throw new CurrencyMismatchException(source.Currency, destination.Currency);

        source.Debit(amount);
        destination.Credit(amount);

        var transfer = new Transfer
        {
            Id = Guid.NewGuid(),
            SourceAccountId = source.Id,
            DestinationAccountId = destination.Id,
            Amount = amount,
            Currency = source.Currency,
            Description = description,
            Status = TransferStatus.Completed,
            CreatedAt = occurredAt,
        };

        var debitEntry = LedgerEntry.Create(transfer.Id, source.Id, EntryDirection.Debit, amount, source.Balance, occurredAt);
        var creditEntry = LedgerEntry.Create(transfer.Id, destination.Id, EntryDirection.Credit, amount, destination.Balance, occurredAt);

        var @event = new TransferCompleted(
            transfer.Id,
            source.Id,
            destination.Id,
            amount,
            transfer.Currency,
            occurredAt);

        return new TransferResult(transfer, debitEntry, creditEntry, @event);
    }
}

public sealed record TransferResult(
    Transfer Transfer,
    LedgerEntry DebitEntry,
    LedgerEntry CreditEntry,
    TransferCompleted Event);
