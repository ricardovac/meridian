using System.Net;
using System.Net.Http.Json;
using Meridian.Application.Common;
using Meridian.Application.Dtos;
using Meridian.Domain.Entities;
using Meridian.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Api;

public sealed class TransferEndpointsTests : IClassFixture<MeridianApiFactory>
{
    private readonly MeridianApiFactory _factory;

    public TransferEndpointsTests(MeridianApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Transfer_MovesFunds_AndWritesTwoLedgerEntries()
    {
        var sender = await _factory.RegisterUserAsync();
        var receiver = await _factory.RegisterUserAsync();
        var sourceAccount = await sender.GetMainAccountAsync();
        var destinationAccount = await receiver.GetMainAccountAsync();

        var response = await sender.PostTransferAsync(Guid.NewGuid().ToString(), new
        {
            sourceAccountId = sourceAccount.Id,
            destinationAccountId = destinationAccount.Id,
            amount = 250m,
            description = "Dinner split",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var transfer = await response.ReadAsAsync<TransferDto>();
        Assert.Equal(250m, transfer!.Amount);
        Assert.Equal(TransferStatus.Completed, transfer.Status);
        Assert.Equal("Dinner split", transfer.Description);

        var updatedSource = await sender.GetFromJsonAsync<AccountDto>(
            $"/api/accounts/{sourceAccount.Id}", ApiClientExtensions.Json);
        var updatedDestination = await receiver.GetFromJsonAsync<AccountDto>(
            $"/api/accounts/{destinationAccount.Id}", ApiClientExtensions.Json);
        Assert.Equal(750m, updatedSource!.Balance);
        Assert.Equal(1250m, updatedDestination!.Balance);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        var entries = await context.LedgerEntries
            .Where(e => e.TransferId == transfer.Id)
            .OrderBy(e => e.Direction)
            .ToListAsync();

        Assert.Equal(2, entries.Count);
        Assert.Equal(750m, entries.Single(e => e.Direction == EntryDirection.Debit).BalanceAfter);
        Assert.Equal(1250m, entries.Single(e => e.Direction == EntryDirection.Credit).BalanceAfter);

        var outboxMessage = await context.OutboxMessages
            .SingleAsync(m => m.Payload.Contains(transfer.Id.ToString()));
        Assert.Equal("TransferCompleted", outboxMessage.Type);
        Assert.Null(outboxMessage.ProcessedAt);
    }

    [Fact]
    public async Task Transfer_InsufficientFunds_Returns422_WithProblemType()
    {
        var sender = await _factory.RegisterUserAsync();
        var receiver = await _factory.RegisterUserAsync();
        var sourceAccount = await sender.GetMainAccountAsync();
        var destinationAccount = await receiver.GetMainAccountAsync();

        var response = await sender.PostTransferAsync(Guid.NewGuid().ToString(), new
        {
            sourceAccountId = sourceAccount.Id,
            destinationAccountId = destinationAccount.Id,
            amount = 5000m,
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.ReadAsAsync<ProblemResponse>();
        Assert.Equal("insufficient-funds", problem!.Type);

        var unchanged = await sender.GetFromJsonAsync<AccountDto>(
            $"/api/accounts/{sourceAccount.Id}", ApiClientExtensions.Json);
        Assert.Equal(1000m, unchanged!.Balance);
    }

    [Fact]
    public async Task Transfer_CurrencyMismatch_Returns422()
    {
        var client = await _factory.RegisterUserAsync();
        var sourceAccount = await client.GetMainAccountAsync();
        var usdResponse = await client.PostAsJsonAsync("/api/accounts", new { name = "Dollars", currency = "USD" });
        var usdAccount = await usdResponse.ReadAsAsync<AccountDto>();

        var response = await client.PostTransferAsync(Guid.NewGuid().ToString(), new
        {
            sourceAccountId = sourceAccount.Id,
            destinationAccountId = usdAccount!.Id,
            amount = 10m,
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.ReadAsAsync<ProblemResponse>();
        Assert.Equal("currency-mismatch", problem!.Type);
    }

    [Fact]
    public async Task Transfer_FromAccountNotOwned_Returns404()
    {
        var owner = await _factory.RegisterUserAsync();
        var attacker = await _factory.RegisterUserAsync();
        var ownerAccount = await owner.GetMainAccountAsync();
        var attackerAccount = await attacker.GetMainAccountAsync();

        var response = await attacker.PostTransferAsync(Guid.NewGuid().ToString(), new
        {
            sourceAccountId = ownerAccount.Id,
            destinationAccountId = attackerAccount.Id,
            amount = 100m,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Transfer_ToSameAccount_Returns400()
    {
        var client = await _factory.RegisterUserAsync();
        var account = await client.GetMainAccountAsync();

        var response = await client.PostTransferAsync(Guid.NewGuid().ToString(), new
        {
            sourceAccountId = account.Id,
            destinationAccountId = account.Id,
            amount = 10m,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Transfers_CanBeListedByAccount()
    {
        var sender = await _factory.RegisterUserAsync();
        var receiver = await _factory.RegisterUserAsync();
        var sourceAccount = await sender.GetMainAccountAsync();
        var destinationAccount = await receiver.GetMainAccountAsync();

        var created = await sender.PostTransferAsync(Guid.NewGuid().ToString(), new
        {
            sourceAccountId = sourceAccount.Id,
            destinationAccountId = destinationAccount.Id,
            amount = 50m,
        });
        var transfer = await created.ReadAsAsync<TransferDto>();

        var list = await sender.GetFromJsonAsync<PagedResult<TransferDto>>(
            $"/api/transfers?accountId={sourceAccount.Id}", ApiClientExtensions.Json);

        Assert.Equal(2, list!.Total);
        Assert.Equal(transfer!.Id, list.Items[0].Id);

        var fetched = await sender.GetFromJsonAsync<TransferDto>(
            $"/api/transfers/{transfer.Id}", ApiClientExtensions.Json);
        Assert.Equal(transfer.Id, fetched!.Id);
    }
}
