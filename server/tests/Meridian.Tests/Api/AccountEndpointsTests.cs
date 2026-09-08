using System.Net;
using System.Net.Http.Json;
using Meridian.Application.Dtos;
using Meridian.Domain.Entities;

namespace Meridian.Tests.Api;

public sealed class AccountEndpointsTests : IClassFixture<MeridianApiFactory>
{
    private readonly MeridianApiFactory _factory;

    public AccountEndpointsTests(MeridianApiFactory factory) => _factory = factory;

    [Fact]
    public async Task CreateAccount_Returns201_AndIsRetrievable()
    {
        var client = await _factory.RegisterUserAsync();

        var response = await client.PostAsJsonAsync("/api/accounts", new { name = "Savings", currency = "usd" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.ReadAsAsync<AccountDto>();
        Assert.Equal("Savings", created!.Name);
        Assert.Equal("USD", created.Currency);
        Assert.Equal(0m, created.Balance);

        var fetched = await client.GetFromJsonAsync<AccountDto>(
            $"/api/accounts/{created.Id}", ApiClientExtensions.Json);
        Assert.Equal(created.Id, fetched!.Id);
    }

    [Fact]
    public async Task GetAccount_OwnedByAnotherUser_Returns404()
    {
        var owner = await _factory.RegisterUserAsync();
        var ownerAccount = await owner.GetMainAccountAsync();
        var stranger = await _factory.RegisterUserAsync();

        var response = await stranger.GetAsync($"/api/accounts/{ownerAccount.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Deposit_CreatesDoubleEntry_AndIncreasesBalance()
    {
        var client = await _factory.RegisterUserAsync();
        var account = await client.GetMainAccountAsync();

        var response = await client.PostDepositAsync(account.Id, 250.50m, Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var transfer = await response.ReadAsAsync<TransferDto>();
        Assert.Equal(account.Id, transfer!.DestinationAccountId);
        Assert.Equal(250.50m, transfer.Amount);
        Assert.Equal(TransferStatus.Completed, transfer.Status);

        var updated = await client.GetFromJsonAsync<AccountDto>(
            $"/api/accounts/{account.Id}", ApiClientExtensions.Json);
        Assert.Equal(1250.50m, updated!.Balance);
    }

    [Fact]
    public async Task Entries_ArePaginated_NewestFirst()
    {
        var client = await _factory.RegisterUserAsync();
        var account = await client.GetMainAccountAsync();

        foreach (var amount in new[] { 10m, 20m, 30m })
        {
            var deposit = await client.PostDepositAsync(account.Id, amount, Guid.NewGuid().ToString());
            Assert.Equal(HttpStatusCode.Created, deposit.StatusCode);
        }

        var firstPage = await client.ReadEntriesAsync(account.Id, page: 1, pageSize: 2);
        Assert.Equal(4, firstPage.Total);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Equal(1, firstPage.Page);
        Assert.Equal(2, firstPage.PageSize);
        Assert.Equal(1060m, firstPage.Items[0].BalanceAfter);

        var lastPage = await client.ReadEntriesAsync(account.Id, page: 2, pageSize: 2);
        Assert.Equal(2, lastPage.Items.Count);
        Assert.Equal(1000m, lastPage.Items[^1].BalanceAfter);
    }
}
