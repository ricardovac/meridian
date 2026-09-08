using Meridian.Application.Exceptions;
using Meridian.Domain.Entities;
using Meridian.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Tests.Infrastructure;

public sealed class ConcurrencyTokenTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<MeridianDbContext> _options;

    public ConcurrencyTokenTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<MeridianDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new MeridianDbContext(_options);
        context.Database.EnsureCreated();
    }

    [Fact]
    public async Task StaleVersion_TriggersConcurrencyConflict_OnSqlite()
    {
        Guid sourceId, destinationId;
        await using (var setup = new MeridianDbContext(_options))
        {
            var system = Account.CreateSystem("BRL", Now);
            var source = Account.CreateForUser(Guid.NewGuid(), "Source", "BRL", Now);
            var destination = Account.CreateForUser(Guid.NewGuid(), "Destination", "BRL", Now);
            Transfer.Execute(system, source, 1000m, "Seed", Now);
            setup.AddRange(system, source, destination);
            await setup.SaveChangesAsync();
            sourceId = source.Id;
            destinationId = destination.Id;
        }

        await using var contextA = new MeridianDbContext(_options);
        await using var contextB = new MeridianDbContext(_options);
        var unitOfWorkA = new EfUnitOfWork(contextA);
        var unitOfWorkB = new EfUnitOfWork(contextB);

        var sourceA = await contextA.Accounts.SingleAsync(a => a.Id == sourceId);
        var destinationA = await contextA.Accounts.SingleAsync(a => a.Id == destinationId);
        var sourceB = await contextB.Accounts.SingleAsync(a => a.Id == sourceId);
        var destinationB = await contextB.Accounts.SingleAsync(a => a.Id == destinationId);

        var first = Transfer.Execute(sourceA, destinationA, 100m, null, Now);
        contextA.Add(first.Transfer);
        contextA.AddRange(first.DebitEntry, first.CreditEntry);
        await unitOfWorkA.SaveChangesAsync();

        var second = Transfer.Execute(sourceB, destinationB, 100m, null, Now);
        contextB.Add(second.Transfer);
        contextB.AddRange(second.DebitEntry, second.CreditEntry);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => unitOfWorkB.SaveChangesAsync());
    }

    public void Dispose() => _connection.Dispose();
}
