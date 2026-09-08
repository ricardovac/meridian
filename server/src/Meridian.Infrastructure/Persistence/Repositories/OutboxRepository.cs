using Meridian.Application.Abstractions;
using Meridian.Domain.Entities;

namespace Meridian.Infrastructure.Persistence.Repositories;

public sealed class OutboxRepository : IOutboxRepository
{
    private readonly MeridianDbContext _context;

    public OutboxRepository(MeridianDbContext context) => _context = context;

    public void Add(OutboxMessage message) => _context.OutboxMessages.Add(message);
}
