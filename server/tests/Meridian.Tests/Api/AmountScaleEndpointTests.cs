using System.Net;
using Meridian.Application.Dtos;

namespace Meridian.Tests.Api;

public sealed class AmountScaleEndpointTests : IClassFixture<MeridianApiFactory>
{
    private readonly MeridianApiFactory _factory;

    public AmountScaleEndpointTests(MeridianApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Transfer_WithMoreThanTwoDecimals_Returns422_AndMovesNoMoney()
    {
        var sender = await _factory.RegisterUserAsync();
        var receiver = await _factory.RegisterUserAsync();
        var sourceAccount = await sender.GetMainAccountAsync();
        var destinationAccount = await receiver.GetMainAccountAsync();

        var response = await sender.PostTransferAsync(Guid.NewGuid().ToString(), new
        {
            sourceAccountId = sourceAccount.Id,
            destinationAccountId = destinationAccount.Id,
            amount = 10.005m,
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.ReadAsAsync<ProblemResponse>();
        Assert.Equal("invalid-amount-scale", problem!.Type);

        Assert.Equal(1000m, (await sender.GetMainAccountAsync()).Balance);
        Assert.Equal(1000m, (await receiver.GetMainAccountAsync()).Balance);
    }

    [Fact]
    public async Task Transfer_WithExactlyTwoDecimals_IsAccepted()
    {
        var sender = await _factory.RegisterUserAsync();
        var receiver = await _factory.RegisterUserAsync();
        var sourceAccount = await sender.GetMainAccountAsync();
        var destinationAccount = await receiver.GetMainAccountAsync();

        var response = await sender.PostTransferAsync(Guid.NewGuid().ToString(), new
        {
            sourceAccountId = sourceAccount.Id,
            destinationAccountId = destinationAccount.Id,
            amount = 10.01m,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var transfer = await response.ReadAsAsync<TransferDto>();
        Assert.Equal(10.01m, transfer!.Amount);
        Assert.Equal(989.99m, (await sender.GetMainAccountAsync()).Balance);
    }

    [Fact]
    public async Task Deposit_WithMoreThanTwoDecimals_Returns422_AndCreditsNothing()
    {
        var client = await _factory.RegisterUserAsync();
        var account = await client.GetMainAccountAsync();

        var response = await client.PostDepositAsync(account.Id, 10.005m, Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.ReadAsAsync<ProblemResponse>();
        Assert.Equal("invalid-amount-scale", problem!.Type);

        Assert.Equal(1000m, (await client.GetMainAccountAsync()).Balance);
    }
}
