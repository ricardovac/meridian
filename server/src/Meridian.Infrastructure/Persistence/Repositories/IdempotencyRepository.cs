using Meridian.Application.Abstractions;
using Meridian.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Infrastructure.Persistence.Repositories;

public sealed class IdempotencyRepository : IIdempotencyRepository
{
    private readonly MeridianDbContext _context;

    public IdempotencyRepository(MeridianDbContext context) => _context = context;

    public Task<IdempotencyRecord?> FindAsync(Guid userId, string key, CancellationToken cancellationToken = default) =>
        _context.IdempotencyRecords.FirstOrDefaultAsync(
            r => r.UserId == userId && r.Key == key, cancellationToken);

    public void Add(IdempotencyRecord record) => _context.IdempotencyRecords.Add(record);

    public void Remove(IdempotencyRecord record) => _context.IdempotencyRecords.Remove(record);
}
