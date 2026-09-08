using Meridian.Domain.Entities;

namespace Meridian.Application.Dtos;

public sealed record AuthResult(string Token);

public sealed record AccountDto(
    Guid Id,
    string Name,
    string Currency,
    decimal Balance,
    bool IsSystem,
    DateTime CreatedAt)
{
    public static AccountDto From(Account account) => new(
        account.Id, account.Name, account.Currency, account.Balance, account.IsSystem, account.CreatedAt);
}

public sealed record TransferDto(
    Guid Id,
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount,
    string Currency,
    string? Description,
    TransferStatus Status,
    DateTime CreatedAt)
{
    public static TransferDto From(Transfer transfer) => new(
        transfer.Id, transfer.SourceAccountId, transfer.DestinationAccountId, transfer.Amount,
        transfer.Currency, transfer.Description, transfer.Status, transfer.CreatedAt);
}

public sealed record LedgerEntryDto(
    Guid Id,
    Guid TransferId,
    Guid AccountId,
    EntryDirection Direction,
    decimal Amount,
    decimal BalanceAfter,
    DateTime CreatedAt)
{
    public static LedgerEntryDto From(LedgerEntry entry) => new(
        entry.Id, entry.TransferId, entry.AccountId, entry.Direction,
        entry.Amount, entry.BalanceAfter, entry.CreatedAt);
}
