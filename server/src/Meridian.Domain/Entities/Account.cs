using System.ComponentModel.DataAnnotations;
using Meridian.Domain.Exceptions;

namespace Meridian.Domain.Entities;

public class Account
{
    public Guid Id { get; private set; }
    public Guid? OwnerUserId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Currency { get; private set; } = null!;
    public decimal Balance { get; private set; }

    [ConcurrencyCheck]
    public int Version { get; private set; }

    public bool IsSystem { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private Account() { }

    public static Account CreateForUser(Guid ownerUserId, string name, string currency, DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainValidationException("Account name is required.");

        return new Account
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            Name = name.Trim(),
            Currency = NormalizeCurrency(currency),
            Balance = 0m,
            Version = 0,
            IsSystem = false,
            CreatedAt = createdAt,
        };
    }

    public static Account CreateSystem(string currency, DateTime createdAt)
    {
        var normalized = NormalizeCurrency(currency);
        return new Account
        {
            Id = Guid.NewGuid(),
            OwnerUserId = null,
            Name = $"System {normalized}",
            Currency = normalized,
            Balance = 0m,
            Version = 0,
            IsSystem = true,
            CreatedAt = createdAt,
        };
    }

    internal void Debit(decimal amount)
    {
        if (!IsSystem && Balance < amount)
            throw new InsufficientFundsException(Id, Balance, amount);

        Balance -= amount;
        Version++;
    }

    internal void Credit(decimal amount)
    {
        Balance += amount;
        Version++;
    }

    private static string NormalizeCurrency(string currency)
    {
        if (currency is null || currency.Trim().Length != 3 || !currency.Trim().All(char.IsLetter))
            throw new DomainValidationException("Currency must be a 3-letter ISO code.");

        return currency.Trim().ToUpperInvariant();
    }
}
