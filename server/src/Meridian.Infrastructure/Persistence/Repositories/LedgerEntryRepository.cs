using Meridian.Application.Abstractions;
using Meridian.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Infrastructure.Persistence.Repositories;

public sealed class LedgerEntryRepository : ILedgerEntryRepository
{
    private readonly MeridianDbContext _context;

    public LedgerEntryRepository(MeridianDbContext context) => _context = context;

    public void AddRange(IEnumerable<LedgerEntry> entries) => _context.LedgerEntries.AddRange(entries);

    public async Task<(IReadOnlyList<LedgerEntry> Items, int Total)> GetPageAsync(
        Guid accountId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _context.LedgerEntries.Where(e => e.AccountId == accountId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }
}
