using System.Net;
using Meridian.Application.Dtos;
using Meridian.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Api;

public sealed class IdempotencyTests : IClassFixture<MeridianApiFactory>
{
    private readonly MeridianApiFactory _factory;

    public IdempotencyTests(MeridianApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Replay_ReturnsFirstResponse_AndDoesNotDuplicateTheTransfer()
    {
        var sender = await _factory.RegisterUserAsync();
        var receiver = await _factory.RegisterUserAsync();
        var sourceAccount = await sender.GetMainAccountAsync();
        var destinationAccount = await receiver.GetMainAccountAsync();

        var key = Guid.NewGuid().ToString();
        var payload = new
        {
            sourceAccountId = sourceAccount.Id,
            destinationAccountId = destinationAccount.Id,
            amount = 250m,
            description = "Only once",
        };

        var first = await sender.PostTransferAsync(key, payload);
        var second = await sender.PostTransferAsync(key, payload);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var firstTransfer = await first.ReadAsAsync<TransferDto>();
        var secondTransfer = await second.ReadAsAsync<TransferDto>();
        Assert.Equal(firstTransfer!.Id, secondTransfer!.Id);

        var balance = (await sender.GetMainAccountAsync()).Balance;
        Assert.Equal(750m, balance);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        var transferCount = await context.Transfers.CountAsync(
            t => t.SourceAccountId == sourceAccount.Id && t.DestinationAccountId == destinationAccount.Id);
        Assert.Equal(1, transferCount);
    }

    [Fact]
    public async Task SameKey_WithDifferentPayload_Returns422()
    {
        var sender = await _factory.RegisterUserAsync();
        var receiver = await _factory.RegisterUserAsync();
        var sourceAccount = await sender.GetMainAccountAsync();
        var destinationAccount = await receiver.GetMainAccountAsync();

        var key = Guid.NewGuid().ToString();
        var first = await sender.PostTransferAsync(key, new
        {
            sourceAccountId = sourceAccount.Id,
            destinationAccountId = destinationAccount.Id,
            amount = 100m,
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await sender.PostTransferAsync(key, new
        {
            sourceAccountId = sourceAccount.Id,
            destinationAccountId = destinationAccount.Id,
            amount = 999m,
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        var problem = await second.ReadAsAsync<ProblemResponse>();
        Assert.Equal("idempotency-key-conflict", problem!.Type);

        var balance = (await sender.GetMainAccountAsync()).Balance;
        Assert.Equal(900m, balance);
    }

    [Fact]
    public async Task MissingIdempotencyKey_Returns400()
    {
        var sender = await _factory.RegisterUserAsync();
        var receiver = await _factory.RegisterUserAsync();
        var sourceAccount = await sender.GetMainAccountAsync();
        var destinationAccount = await receiver.GetMainAccountAsync();

        var response = await sender.PostAsync("/api/transfers", System.Net.Http.Json.JsonContent.Create(new
        {
            sourceAccountId = sourceAccount.Id,
            destinationAccountId = destinationAccount.Id,
            amount = 10m,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SameKey_ForDifferentUsers_IsIndependent()
    {
        var key = $"shared-{Guid.NewGuid():N}";

        var userA = await _factory.RegisterUserAsync();
        var userB = await _factory.RegisterUserAsync();
        var accountA = await userA.GetMainAccountAsync();
        var accountB = await userB.GetMainAccountAsync();

        var depositA = await userA.PostDepositAsync(accountA.Id, 10m, key);
        var depositB = await userB.PostDepositAsync(accountB.Id, 20m, key);

        Assert.Equal(HttpStatusCode.Created, depositA.StatusCode);
        Assert.Equal(HttpStatusCode.Created, depositB.StatusCode);
        Assert.Equal(1010m, (await userA.GetMainAccountAsync()).Balance);
        Assert.Equal(1020m, (await userB.GetMainAccountAsync()).Balance);
    }

    [Fact]
    public async Task Deposit_Replay_DoesNotDoubleCredit()
    {
        var client = await _factory.RegisterUserAsync();
        var account = await client.GetMainAccountAsync();
        var key = Guid.NewGuid().ToString();

        var first = await client.PostDepositAsync(account.Id, 100m, key);
        var second = await client.PostDepositAsync(account.Id, 100m, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var firstTransfer = await first.ReadAsAsync<TransferDto>();
        var secondTransfer = await second.ReadAsAsync<TransferDto>();
        Assert.Equal(firstTransfer!.Id, secondTransfer!.Id);
        Assert.Equal(1100m, (await client.GetMainAccountAsync()).Balance);
    }

    [Fact]
    public async Task FailedRequest_IsNotRecorded_SoRetryCanSucceed()
    {
        var sender = await _factory.RegisterUserAsync();
        var receiver = await _factory.RegisterUserAsync();
        var sourceAccount = await sender.GetMainAccountAsync();
        var destinationAccount = await receiver.GetMainAccountAsync();
        var key = Guid.NewGuid().ToString();

        var failed = await sender.PostTransferAsync(key, new
        {
            sourceAccountId = sourceAccount.Id,
            destinationAccountId = destinationAccount.Id,
            amount = 99999m,
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, failed.StatusCode);

        var retried = await sender.PostTransferAsync(key, new
        {
            sourceAccountId = sourceAccount.Id,
            destinationAccountId = destinationAccount.Id,
            amount = 100m,
        });

        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
        Assert.Equal(900m, (await sender.GetMainAccountAsync()).Balance);
    }
}
