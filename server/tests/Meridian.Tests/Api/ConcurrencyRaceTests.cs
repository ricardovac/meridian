using System.Net;
using System.Net.Http.Json;
using Meridian.Application.Dtos;
using Meridian.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Api;

public sealed class ConcurrencyRaceTests : IClassFixture<MeridianApiFactory>
{
    private const int Racers = 5;

    private readonly MeridianApiFactory _factory;

    public ConcurrencyRaceTests(MeridianApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ConcurrentDeposits_InABrandNewCurrency_CreateExactlyOneSystemAccount()
    {
        const string currency = "XAA";
        var accounts = await CreateAccountsInAsync(currency);

        var responses = await Task.WhenAll(accounts.Select(
            entry => entry.Client.PostDepositAsync(entry.Account.Id, 100m, Guid.NewGuid().ToString())));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        var systemAccounts = await context.Accounts
            .Where(a => a.IsSystem && a.Currency == currency)
            .ToListAsync();

        var systemAccount = Assert.Single(systemAccounts);
        Assert.Equal(-100m * Racers, systemAccount.Balance);
        Assert.Equal(
            Racers,
            await context.LedgerEntries.CountAsync(e => e.AccountId == systemAccount.Id));
    }

    [Fact]
    public async Task ConcurrentDeposits_InABrandNewCurrency_CreditEveryAccountExactlyOnce()
    {
        const string currency = "XAB";
        var accounts = await CreateAccountsInAsync(currency);

        var responses = await Task.WhenAll(accounts.Select(
            entry => entry.Client.PostDepositAsync(entry.Account.Id, 100m, Guid.NewGuid().ToString())));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));

        foreach (var entry in accounts)
        {
            var refreshed = await entry.Client.GetFromJsonAsync<AccountDto>(
                $"/api/accounts/{entry.Account.Id}", ApiClientExtensions.Json);
            Assert.Equal(100m, refreshed!.Balance);
        }
    }

    [Fact]
    public async Task ConcurrentRegistrations_AllSucceed_DespiteRacingOnTheSystemAccountBalance()
    {
        const string emailPrefix = "race-register-";
        var balanceBefore = await SystemBalanceAsync("BRL");
        var clients = Enumerable.Range(0, Racers).Select(_ => _factory.CreateClient()).ToList();

        var responses = await Task.WhenAll(clients.Select((client, index) => client.PostAsJsonAsync(
            "/api/auth/register",
            new { email = $"{emailPrefix}{index}@meridian.dev", password = "s3cret-password" })));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        var userIds = await context.Users
            .Where(u => u.Email.StartsWith(emailPrefix))
            .Select(u => u.Id)
            .ToListAsync();
        Assert.Equal(Racers, userIds.Count);

        var openedAccounts = await context.Accounts
            .Where(a => a.OwnerUserId != null && userIds.Contains(a.OwnerUserId!.Value))
            .ToListAsync();
        Assert.Equal(Racers, openedAccounts.Count);
        Assert.All(openedAccounts, account => Assert.Equal(1000m, account.Balance));

        Assert.Equal(balanceBefore - (1000m * Racers), await SystemBalanceAsync("BRL"));
    }

    [Fact]
    public async Task ConcurrentRegistrations_WithTheSameEmail_ProduceOneCreatedAndOneConflict()
    {
        const string email = "race-duplicate-email@meridian.dev";
        var first = _factory.CreateClient();
        var second = _factory.CreateClient();
        var payload = new { email, password = "s3cret-password" };

        var responses = await Task.WhenAll(
            first.PostAsJsonAsync("/api/auth/register", payload),
            second.PostAsJsonAsync("/api/auth/register", payload));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var conflict = responses.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        var problem = await conflict.ReadAsAsync<ProblemResponse>();
        Assert.Equal("conflict", problem!.Type);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        Assert.Equal(1, await context.Users.CountAsync(u => u.Email == email));
    }

    private async Task<decimal> SystemBalanceAsync(string currency)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();
        var systemAccounts = await context.Accounts
            .Where(a => a.IsSystem && a.Currency == currency)
            .ToListAsync();

        return systemAccounts.Count == 0 ? 0m : Assert.Single(systemAccounts).Balance;
    }

    private async Task<IReadOnlyList<(HttpClient Client, AccountDto Account)>> CreateAccountsInAsync(string currency)
    {
        var entries = new List<(HttpClient, AccountDto)>();
        for (var i = 0; i < Racers; i++)
        {
            var client = await _factory.RegisterUserAsync($"race-{currency.ToLowerInvariant()}-{i}@meridian.dev");
            var response = await client.PostAsJsonAsync("/api/accounts", new { name = currency, currency });
            response.EnsureSuccessStatusCode();
            entries.Add((client, (await response.ReadAsAsync<AccountDto>())!));
        }

        return entries;
    }
}
