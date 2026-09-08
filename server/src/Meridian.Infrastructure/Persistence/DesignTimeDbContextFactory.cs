using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Meridian.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MeridianDbContext>
{
    public MeridianDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MeridianDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=meridian;Username=meridian;Password=meridian")
            .Options;

        return new MeridianDbContext(options);
    }
}
