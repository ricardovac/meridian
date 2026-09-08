using System.Security.Claims;
using Meridian.Api.Filters;
using Meridian.Application.Abstractions;
using Meridian.Application.Exceptions;
using Meridian.Domain.Entities;
using Meridian.Infrastructure.Persistence;
using Meridian.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meridian.Tests.Api;

public sealed class IdempotencyFilterTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _keepAlive;
    private readonly DbContextOptions<MeridianDbContext> _options;
    private readonly Guid _userId = Guid.NewGuid();

    public IdempotencyFilterTests()
    {
        var connectionString = $"DataSource=filter-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();
        _options = new DbContextOptionsBuilder<MeridianDbContext>().UseSqlite(connectionString).Options;

        using var context = new MeridianDbContext(_options);
        context.Database.EnsureCreated();
    }

    [Fact]
    public async Task Commit_CoversA2xxStatusCodeResult_WithoutABody()
    {
        const string key = "no-content-action";
        await using var context = new MeridianDbContext(_options);

        await InvokeAsync(context, key, () =>
        {
            context.Accounts.Add(Account.CreateForUser(_userId, "Written by the action", "BRL", Now));
            context.SaveChanges();
            return new NoContentResult();
        });

        await using var assertions = new MeridianDbContext(_options);
        var record = await assertions.IdempotencyRecords.SingleAsync(r => r.Key == key);
        Assert.True(record.IsCompleted);
        Assert.Equal(StatusCodes.Status204NoContent, record.ResponseStatusCode);
        Assert.Equal(string.Empty, record.ResponseBody);
        Assert.True(
            await assertions.Accounts.AnyAsync(a => a.Name == "Written by the action"),
            "The action's write was rolled back: the filter did not commit a 2xx StatusCodeResult.");
    }

    [Fact]
    public async Task Replay_OfACommittedNoContentResponse_ReturnsTheStoredStatus()
    {
        const string key = "no-content-replay";
        await using (var first = new MeridianDbContext(_options))
        {
            await InvokeAsync(first, key, () => new NoContentResult());
        }

        await using var second = new MeridianDbContext(_options);
        var replayed = await InvokeAsync(second, key, () => throw new InvalidOperationException(
            "The action must not run again on replay."));

        var content = Assert.IsType<ContentResult>(replayed);
        Assert.Equal(StatusCodes.Status204NoContent, content.StatusCode);
    }

    [Fact]
    public async Task NonSuccessStatusCodeResult_RollsBackTheAction_AndReleasesTheReservation()
    {
        const string key = "bad-request-action";
        await using var context = new MeridianDbContext(_options);

        await InvokeAsync(context, key, () =>
        {
            context.Accounts.Add(Account.CreateForUser(_userId, "Rolled back", "BRL", Now));
            context.SaveChanges();
            return new BadRequestResult();
        });

        await using var assertions = new MeridianDbContext(_options);
        Assert.False(await assertions.Accounts.AnyAsync(a => a.Name == "Rolled back"));
        Assert.False(await assertions.IdempotencyRecords.AnyAsync(r => r.Key == key));
    }

    [Fact]
    public async Task FailedAction_WithStaleTrackedEntities_ReleasesTheReservation_AndKeepsTheOriginalException()
    {
        const string key = "exhausted-retry-budget";
        var accountId = await SeedFundedAccountAsync();

        await using var context = new MeridianDbContext(_options);
        var executed = await InvokeFailingActionAsync(context, key, accountId);

        Assert.IsType<ConcurrencyConflictException>(executed.Exception);
        Assert.False(executed.ExceptionHandled);

        await using var assertions = new MeridianDbContext(_options);
        Assert.False(
            await assertions.IdempotencyRecords.AnyAsync(r => r.Key == key),
            "The reservation stayed behind: every retry with this key would answer 409 idempotency-in-flight forever.");
    }

    [Fact]
    public async Task FailedAction_WithStaleTrackedEntities_LeavesTheKeyUsableForARetry()
    {
        const string key = "retry-after-exhausted-budget";
        var accountId = await SeedFundedAccountAsync();

        await using (var failing = new MeridianDbContext(_options))
        {
            await InvokeFailingActionAsync(failing, key, accountId);
        }

        await using var retry = new MeridianDbContext(_options);
        var result = await InvokeAsync(retry, key, () => new NoContentResult());

        Assert.Null(result);

        await using var assertions = new MeridianDbContext(_options);
        var record = await assertions.IdempotencyRecords.SingleAsync(r => r.Key == key);
        Assert.True(record.IsCompleted);
    }

    private async Task<ActionExecutedContext> InvokeFailingActionAsync(
        MeridianDbContext context, string key, Guid accountId)
    {
        var staleAccount = await context.Accounts.SingleAsync(a => a.Id == accountId);
        var staleSystem = await context.Accounts.SingleAsync(a => a.IsSystem);

        await using (var competitor = new MeridianDbContext(_options))
        {
            var account = await competitor.Accounts.SingleAsync(a => a.Id == accountId);
            var system = await competitor.Accounts.SingleAsync(a => a.IsSystem);
            Transfer.Execute(system, account, 5m, "Concurrent winner", Now);
            await competitor.SaveChangesAsync();
        }

        ActionExecutedContext? executed = null;
        await InvokeAsync(
            context,
            key,
            () =>
            {
                Transfer.Execute(staleSystem, staleAccount, 10m, "Loser", Now);
                throw new ConcurrencyConflictException();
            },
            captureExecuted: value => executed = value);

        return executed!;
    }

    private async Task<Guid> SeedFundedAccountAsync()
    {
        await using var context = new MeridianDbContext(_options);
        var system = Account.CreateSystem("BRL", Now);
        var account = Account.CreateForUser(_userId, "Main", "BRL", Now);
        Transfer.Execute(system, account, 1000m, "Seed", Now);
        context.Accounts.AddRange(system, account);
        await context.SaveChangesAsync();
        return account.Id;
    }

    private Task<IActionResult?> InvokeAsync(MeridianDbContext context, string key, Func<IActionResult> action) =>
        InvokeAsync(context, key, action, captureExecuted: null);

    private async Task<IActionResult?> InvokeAsync(
        MeridianDbContext context,
        string key,
        Func<IActionResult> action,
        Action<ActionExecutedContext>? captureExecuted)
    {
        var unitOfWork = new EfUnitOfWork(context);
        var filter = new IdempotencyFilter(
            new IdempotencyRepository(context),
            unitOfWork,
            new FixedClock(),
            Options.Create(new Microsoft.AspNetCore.Mvc.JsonOptions()),
            NullLogger<IdempotencyFilter>.Instance);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[IdempotencyFilter.HeaderName] = key;
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, _userId.ToString()) }, "test"));

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var filters = new List<IFilterMetadata>();
        var executing = new ActionExecutingContext(
            actionContext, filters, new Dictionary<string, object?> { ["amount"] = 100m }, controller: this);

        await filter.OnActionExecutionAsync(executing, () =>
        {
            var executed = new ActionExecutedContext(actionContext, filters, controller: this);
            try
            {
                executed.Result = action();
            }
            catch (Exception ex)
            {
                executed.Exception = ex;
            }

            captureExecuted?.Invoke(executed);
            return Task.FromResult(executed);
        });

        return executing.Result;
    }

    public void Dispose() => _keepAlive.Dispose();

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => Now;
    }
}
