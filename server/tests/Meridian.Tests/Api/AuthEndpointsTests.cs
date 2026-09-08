using System.Net;
using System.Net.Http.Json;
using Meridian.Application.Common;
using Meridian.Application.Dtos;
using Meridian.Domain.Entities;
using Meridian.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Api;

public sealed class AuthEndpointsTests : IClassFixture<MeridianApiFactory>
{
    private readonly MeridianApiFactory _factory;

    public AuthEndpointsTests(MeridianApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_Returns201_WithToken()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"register-{Guid.NewGuid():N}@meridian.dev",
            password = "s3cret-password",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var auth = await response.ReadAsAsync<AuthResult>();
        Assert.False(string.IsNullOrWhiteSpace(auth!.Token));
    }

    [Fact]
    public async Task Register_SeedsMainAccount_With1000OpeningBalance_ViaSystemAccount()
    {
        var client = await _factory.RegisterUserAsync();

        var account = await client.GetMainAccountAsync();
        Assert.Equal("BRL", account.Currency);
        Assert.Equal(1000.00m, account.Balance);

        var entries = await client.ReadEntriesAsync(account.Id);
        var entry = Assert.Single(entries.Items);
        Assert.Equal(EntryDirection.Credit, entry.Direction);
        Assert.Equal(1000.00m, entry.Amount);
        Assert.Equal(1000.00m, entry.BalanceAfter);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();

        var systemAccount = await context.Accounts.SingleAsync(a => a.IsSystem && a.Currency == "BRL");
        Assert.Null(systemAccount.OwnerUserId);
        Assert.True(systemAccount.Balance <= -1000.00m);

        var openingTransfer = await context.Transfers.SingleAsync(t => t.Id == entry.TransferId);
        Assert.Equal(systemAccount.Id, openingTransfer.SourceAccountId);
        Assert.Equal(account.Id, openingTransfer.DestinationAccountId);
        Assert.Equal(2, await context.LedgerEntries.CountAsync(e => e.TransferId == openingTransfer.Id));
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409()
    {
        var email = $"dupe-{Guid.NewGuid():N}@meridian.dev";
        await _factory.RegisterUserAsync(email);

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "s3cret-password" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Register_InvalidPayload_Returns400()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new { email = "not-an-email", password = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsToken()
    {
        var email = $"login-{Guid.NewGuid():N}@meridian.dev";
        await _factory.RegisterUserAsync(email);

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "s3cret-password" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = await response.ReadAsAsync<AuthResult>();
        Assert.False(string.IsNullOrWhiteSpace(auth!.Token));
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        var email = $"wrongpw-{Guid.NewGuid():N}@meridian.dev";
        await _factory.RegisterUserAsync(email);

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Accounts_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/accounts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
