using Meridian.Application.Abstractions;
using Meridian.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Infrastructure.Persistence.Repositories;

public sealed class TransferRepository : ITransferRepository
{
    private readonly MeridianDbContext _context;

    public TransferRepository(MeridianDbContext context) => _context = context;

    public Task<Transfer?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.Transfers.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<Transfer> Items, int Total)> GetPageAsync(
        Guid accountId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _context.Transfers.Where(
            t => t.SourceAccountId == accountId || t.DestinationAccountId == accountId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public void Add(Transfer transfer) => _context.Transfers.Add(transfer);
}
