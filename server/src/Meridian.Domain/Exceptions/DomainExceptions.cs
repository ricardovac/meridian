namespace Meridian.Domain.Exceptions;

public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }
}

public sealed class DomainValidationException : DomainException
{
    public DomainValidationException(string message) : base(message) { }
}

public sealed class InsufficientFundsException : DomainException
{
    public Guid AccountId { get; }
    public decimal Balance { get; }
    public decimal Requested { get; }

    public InsufficientFundsException(Guid accountId, decimal balance, decimal requested)
        : base($"Account {accountId} has insufficient funds: balance {balance}, requested {requested}.")
    {
        AccountId = accountId;
        Balance = balance;
        Requested = requested;
    }
}

public sealed class CurrencyMismatchException : DomainException
{
    public CurrencyMismatchException(string sourceCurrency, string destinationCurrency)
        : base($"Cannot transfer between accounts with different currencies ({sourceCurrency} -> {destinationCurrency}).")
    {
    }
}

public sealed class InvalidAmountScaleException : DomainException
{
    public InvalidAmountScaleException(decimal amount)
        : base($"Amount {amount} has more than 2 decimal places; the ledger does not round monetary values.")
    {
    }
}
