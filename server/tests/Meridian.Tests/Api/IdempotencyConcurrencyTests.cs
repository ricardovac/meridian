using System.Net;
using Meridian.Domain.Entities;
using Meridian.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Api;

public sealed class IdempotencyConcurrencyTests : IClassFixture<MeridianApiFactory>
{
    private readonly MeridianApiFactory _factory;

    public IdempotencyConcurrencyTests(MeridianApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ConcurrentRequests_WithTheSameKey_DepositOnce_AndNeverFailWith500()
    {
        var client = await _factory.RegisterUserAsync("idem-race@meridian.dev");
        var account = await client.GetMainAccountAsync();
        var key = "concurrent-deposit-key";

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => client.PostDepositAsync(account.Id, 100m, key)));

        Assert.All(responses, response => Assert.True(
            response.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict,
            $"Unexpected status {(int)response.StatusCode} for a concurrent replay."));
        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.Created);

        Assert.Equal(1100m, (await client.GetMainAccountAsync()).Balance);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        Assert.Equal(
            1,
            await context.Transfers.CountAsync(t => t.DestinationAccountId == account.Id && t.Amount == 100m));
    }

    [Fact]
    public async Task Request_WithAKeyStillInFlight_Returns409_WithRetryAfter_AndDoesNotExecute()
    {
        var client = await _factory.RegisterUserAsync("idem-in-flight@meridian.dev");
        var account = await client.GetMainAccountAsync();
        const string key = "reserved-but-never-completed";
        await ReserveKeyAsync("idem-in-flight@meridian.dev", key);

        var response = await client.PostDepositAsync(account.Id, 250m, key);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadAsAsync<ProblemResponse>();
        Assert.Equal("idempotency-in-flight", problem!.Type);
        Assert.Equal("1", Assert.Single(response.Headers.GetValues("Retry-After")));

        Assert.Equal(1000m, (await client.GetMainAccountAsync()).Balance);
    }

    [Fact]
    public async Task OversizedKey_Returns422_BeforeTheActionRuns()
    {
        var client = await _factory.RegisterUserAsync("idem-oversized-key@meridian.dev");
        var account = await client.GetMainAccountAsync();
        var key = new string('k', 250);

        var response = await client.PostDepositAsync(account.Id, 500m, key);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.ReadAsAsync<ProblemResponse>();
        Assert.Equal("invalid-idempotency-key", problem!.Type);

        Assert.Equal(1000m, (await client.GetMainAccountAsync()).Balance);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        Assert.False(await context.IdempotencyRecords.AnyAsync(r => r.Key == key));
    }

    [Fact]
    public async Task KeyAtTheMaximumLength_IsAccepted()
    {
        var client = await _factory.RegisterUserAsync("idem-max-length-key@meridian.dev");
        var account = await client.GetMainAccountAsync();
        var key = new string('k', IdempotencyRecord.MaxKeyLength);

        var response = await client.PostDepositAsync(account.Id, 500m, key);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1500m, (await client.GetMainAccountAsync()).Balance);
    }

    private async Task ReserveKeyAsync(string email, string key)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        var user = await context.Users.SingleAsync(u => u.Email == email);

        context.IdempotencyRecords.Add(
            IdempotencyRecord.Reserve(key, user.Id, "any-hash", DateTime.UtcNow));
        await context.SaveChangesAsync();
    }
}
