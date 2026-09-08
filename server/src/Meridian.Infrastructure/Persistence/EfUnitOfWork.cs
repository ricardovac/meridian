using Meridian.Application.Abstractions;
using Meridian.Application.Exceptions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Meridian.Infrastructure.Persistence;

public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly MeridianDbContext _context;

    public EfUnitOfWork(MeridianDbContext context) => _context = context;

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(ex);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            throw new ConflictException("A record with the same unique key already exists.", ex);
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) => ex.InnerException switch
    {
        PostgresException postgres => postgres.SqlState == PostgresErrorCodes.UniqueViolation,
        SqliteException sqlite => sqlite.SqliteExtendedErrorCode is 2067 or 1555,
        _ => false,
    };

    public async Task<ITransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        return new EfTransactionScope(transaction);
    }

    public void ClearTracking() => _context.ChangeTracker.Clear();

    private sealed class EfTransactionScope : ITransactionScope
    {
        private readonly IDbContextTransaction _transaction;

        public EfTransactionScope(IDbContextTransaction transaction) => _transaction = transaction;

        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            _transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => _transaction.DisposeAsync();
    }
}
