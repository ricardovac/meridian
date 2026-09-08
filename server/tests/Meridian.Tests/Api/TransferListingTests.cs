using System.Net;
using System.Net.Http.Json;
using Meridian.Application.Common;
using Meridian.Application.Dtos;

namespace Meridian.Tests.Api;

public sealed class TransferListingTests : IClassFixture<MeridianApiFactory>
{
    private readonly MeridianApiFactory _factory;

    public TransferListingTests(MeridianApiFactory factory) => _factory = factory;

    [Fact]
    public async Task List_WithoutAccountId_Returns200_WithTransfersFromEveryAccountOfTheUser()
    {
        var client = await _factory.RegisterUserAsync();
        var mainAccount = await client.GetMainAccountAsync();

        var secondResponse = await client.PostAsJsonAsync("/api/accounts", new { name = "Savings", currency = "BRL" });
        var secondAccount = await secondResponse.ReadAsAsync<AccountDto>();

        var moved = await client.PostTransferAsync(Guid.NewGuid().ToString(), new
        {
            sourceAccountId = mainAccount.Id,
            destinationAccountId = secondAccount!.Id,
            amount = 40m,
        });
        Assert.Equal(HttpStatusCode.Created, moved.StatusCode);
        var movedTransfer = await moved.ReadAsAsync<TransferDto>();

        var deposited = await client.PostDepositAsync(secondAccount.Id, 60m, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Created, deposited.StatusCode);
        var depositTransfer = await deposited.ReadAsAsync<TransferDto>();

        var response = await client.GetAsync("/api/transfers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.ReadAsAsync<PagedResult<TransferDto>>();
        var ids = page!.Items.Select(t => t.Id).ToList();

        Assert.Contains(movedTransfer!.Id, ids);
        Assert.Contains(depositTransfer!.Id, ids);
        Assert.Equal(3, page.Total);
        Assert.Equal(1, page.Page);
    }

    [Fact]
    public async Task List_WithoutAccountId_ExcludesTransfersOfOtherUsers()
    {
        var caller = await _factory.RegisterUserAsync();
        var stranger = await _factory.RegisterUserAsync();
        var strangerAccount = await stranger.GetMainAccountAsync();

        var strangerDeposit = await stranger.PostDepositAsync(strangerAccount.Id, 25m, Guid.NewGuid().ToString());
        var strangerTransfer = await strangerDeposit.ReadAsAsync<TransferDto>();

        var page = await caller.GetFromJsonAsync<PagedResult<TransferDto>>(
            "/api/transfers", ApiClientExtensions.Json);

        Assert.DoesNotContain(page!.Items, t => t.Id == strangerTransfer!.Id);
        Assert.DoesNotContain(page.Items, t => t.DestinationAccountId == strangerAccount.Id);
    }

    [Fact]
    public async Task List_WithoutAccountId_IsPaginated()
    {
        var client = await _factory.RegisterUserAsync();
        var account = await client.GetMainAccountAsync();

        await client.PostDepositAsync(account.Id, 10m, Guid.NewGuid().ToString());
        await client.PostDepositAsync(account.Id, 20m, Guid.NewGuid().ToString());

        var firstPage = await client.GetFromJsonAsync<PagedResult<TransferDto>>(
            "/api/transfers?page=1&pageSize=2", ApiClientExtensions.Json);

        Assert.Equal(3, firstPage!.Total);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Equal(2, firstPage.PageSize);

        var secondPage = await client.GetFromJsonAsync<PagedResult<TransferDto>>(
            "/api/transfers?page=2&pageSize=2", ApiClientExtensions.Json);

        Assert.Single(secondPage!.Items);
        Assert.DoesNotContain(secondPage.Items, t => firstPage.Items.Any(f => f.Id == t.Id));
    }

    [Fact]
    public async Task List_WithAccountIdOfAnotherUser_StillReturns404()
    {
        var owner = await _factory.RegisterUserAsync();
        var stranger = await _factory.RegisterUserAsync();
        var ownerAccount = await owner.GetMainAccountAsync();

        var response = await stranger.GetAsync($"/api/transfers?accountId={ownerAccount.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/transfers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
