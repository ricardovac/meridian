using Meridian.Domain.Entities;
using Meridian.Domain.Exceptions;

namespace Meridian.Tests.Domain;

public class TransferTests
{
    private static readonly DateTime Now = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    private static Account CreateFunded(decimal balance, string currency = "BRL")
    {
        var account = Account.CreateForUser(Guid.NewGuid(), "Main", currency, Now);
        if (balance > 0)
        {
            var system = Account.CreateSystem(currency, Now);
            Transfer.Execute(system, account, balance, "Seed", Now);
        }

        return account;
    }

    [Fact]
    public void Execute_WritesExactlyTwoEntries_AndMovesBalances()
    {
        var source = CreateFunded(500m);
        var destination = CreateFunded(100m);

        var result = Transfer.Execute(source, destination, 150m, "Rent", Now);

        Assert.Equal(350m, source.Balance);
        Assert.Equal(250m, destination.Balance);

        Assert.Equal(result.Transfer.Id, result.DebitEntry.TransferId);
        Assert.Equal(result.Transfer.Id, result.CreditEntry.TransferId);

        Assert.Equal(source.Id, result.DebitEntry.AccountId);
        Assert.Equal(EntryDirection.Debit, result.DebitEntry.Direction);
        Assert.Equal(150m, result.DebitEntry.Amount);
        Assert.Equal(350m, result.DebitEntry.BalanceAfter);

        Assert.Equal(destination.Id, result.CreditEntry.AccountId);
        Assert.Equal(EntryDirection.Credit, result.CreditEntry.Direction);
        Assert.Equal(150m, result.CreditEntry.Amount);
        Assert.Equal(250m, result.CreditEntry.BalanceAfter);

        Assert.Equal(TransferStatus.Completed, result.Transfer.Status);
        Assert.Equal("BRL", result.Transfer.Currency);
    }

    [Fact]
    public void Execute_RaisesTransferCompletedEvent()
    {
        var source = CreateFunded(500m);
        var destination = CreateFunded(0m);

        var result = Transfer.Execute(source, destination, 200m, null, Now);

        Assert.Equal(result.Transfer.Id, result.Event.TransferId);
        Assert.Equal(source.Id, result.Event.SourceAccountId);
        Assert.Equal(destination.Id, result.Event.DestinationAccountId);
        Assert.Equal(200m, result.Event.Amount);
        Assert.Equal("BRL", result.Event.Currency);
        Assert.Equal(Now, result.Event.OccurredAt);
    }

    [Fact]
    public void Execute_InsufficientFunds_Throws_AndLeavesBalancesUntouched()
    {
        var source = CreateFunded(50m);
        var destination = CreateFunded(0m);

        Assert.Throws<InsufficientFundsException>(
            () => Transfer.Execute(source, destination, 100m, null, Now));

        Assert.Equal(50m, source.Balance);
        Assert.Equal(0m, destination.Balance);
    }

    [Fact]
    public void Execute_SystemSourceAccount_MayGoNegative()
    {
        var system = Account.CreateSystem("BRL", Now);
        var destination = CreateFunded(0m);

        Transfer.Execute(system, destination, 1000m, "Opening balance", Now);

        Assert.Equal(-1000m, system.Balance);
        Assert.Equal(1000m, destination.Balance);
    }

    [Fact]
    public void Execute_CurrencyMismatch_Throws()
    {
        var source = CreateFunded(500m, "BRL");
        var destination = CreateFunded(0m, "USD");

        Assert.Throws<CurrencyMismatchException>(
            () => Transfer.Execute(source, destination, 100m, null, Now));
    }

    [Fact]
    public void Execute_SelfTransfer_Throws()
    {
        var account = CreateFunded(500m);

        Assert.Throws<DomainValidationException>(
            () => Transfer.Execute(account, account, 100m, null, Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-25)]
    public void Execute_NonPositiveAmount_Throws(decimal amount)
    {
        var source = CreateFunded(500m);
        var destination = CreateFunded(0m);

        Assert.Throws<DomainValidationException>(
            () => Transfer.Execute(source, destination, amount, null, Now));
    }

    [Theory]
    [InlineData(10.005)]
    [InlineData(0.001)]
    [InlineData(99.999)]
    public void Execute_AmountWithMoreThanTwoDecimals_Throws_AndLeavesBalancesUntouched(decimal amount)
    {
        var source = CreateFunded(500m);
        var destination = CreateFunded(0m);

        Assert.Throws<InvalidAmountScaleException>(
            () => Transfer.Execute(source, destination, amount, null, Now));

        Assert.Equal(500m, source.Balance);
        Assert.Equal(0m, destination.Balance);
    }

    [Fact]
    public void Execute_AmountWithExactlyTwoDecimals_IsAccepted()
    {
        var source = CreateFunded(500m);
        var destination = CreateFunded(0m);

        Transfer.Execute(source, destination, 10.99m, null, Now);

        Assert.Equal(489.01m, source.Balance);
        Assert.Equal(10.99m, destination.Balance);
    }

    [Fact]
    public void Execute_BumpsVersion_OnBothAccounts()
    {
        var source = CreateFunded(500m);
        var destination = CreateFunded(0m);
        var sourceVersion = source.Version;
        var destinationVersion = destination.Version;

        Transfer.Execute(source, destination, 100m, null, Now);

        Assert.Equal(sourceVersion + 1, source.Version);
        Assert.Equal(destinationVersion + 1, destination.Version);
    }
}
