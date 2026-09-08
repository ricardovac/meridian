using Meridian.Domain.Entities;
using Meridian.Domain.Exceptions;

namespace Meridian.Tests.Domain;

public class AccountTests
{
    private static readonly DateTime Now = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CreateForUser_NormalizesCurrency()
    {
        var account = Account.CreateForUser(Guid.NewGuid(), "Main", "brl", Now);

        Assert.Equal("BRL", account.Currency);
        Assert.Equal(0m, account.Balance);
        Assert.False(account.IsSystem);
    }

    [Theory]
    [InlineData("BR")]
    [InlineData("BRLX")]
    [InlineData("B1L")]
    [InlineData("")]
    public void CreateForUser_InvalidCurrency_Throws(string currency)
    {
        Assert.Throws<DomainValidationException>(
            () => Account.CreateForUser(Guid.NewGuid(), "Main", currency, Now));
    }

    [Fact]
    public void CreateForUser_BlankName_Throws()
    {
        Assert.Throws<DomainValidationException>(
            () => Account.CreateForUser(Guid.NewGuid(), "  ", "BRL", Now));
    }

    [Fact]
    public void CreateSystem_HasNoOwner()
    {
        var account = Account.CreateSystem("usd", Now);

        Assert.True(account.IsSystem);
        Assert.Null(account.OwnerUserId);
        Assert.Equal("USD", account.Currency);
    }
}
