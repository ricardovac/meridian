namespace Meridian.Domain.Entities;

public class LedgerEntry
{
    public Guid Id { get; private set; }
    public Guid TransferId { get; private set; }
    public Guid AccountId { get; private set; }
    public EntryDirection Direction { get; private set; }
    public decimal Amount { get; private set; }
    public decimal BalanceAfter { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private LedgerEntry() { }

    internal static LedgerEntry Create(
        Guid transferId,
        Guid accountId,
        EntryDirection direction,
        decimal amount,
        decimal balanceAfter,
        DateTime createdAt) => new()
        {
            Id = Guid.NewGuid(),
            TransferId = transferId,
            AccountId = accountId,
            Direction = direction,
            Amount = amount,
            BalanceAfter = balanceAfter,
            CreatedAt = createdAt,
        };
}
