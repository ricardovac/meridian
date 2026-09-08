using Meridian.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Meridian.Infrastructure.Persistence;

public class MeridianDbContext : DbContext
{
    public MeridianDbContext(DbContextOptions<MeridianDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.HasKey(u => u.Id);
            user.Property(u => u.Email).HasMaxLength(320).IsRequired();
            user.HasIndex(u => u.Email).IsUnique();
            user.Property(u => u.PasswordHash).IsRequired();
        });

        modelBuilder.Entity<Account>(account =>
        {
            account.HasKey(a => a.Id);
            account.Property(a => a.Name).HasMaxLength(100).IsRequired();
            account.Property(a => a.Currency).HasMaxLength(3).IsRequired();
            account.Property(a => a.Balance).HasPrecision(18, 2);
            account.HasIndex(a => a.OwnerUserId);
            account.HasIndex(a => a.Currency)
                .IsUnique()
                .HasDatabaseName("IX_Accounts_Currency_IsSystem")
                .HasFilter("\"IsSystem\" = TRUE");
        });

        modelBuilder.Entity<Transfer>(transfer =>
        {
            transfer.HasKey(t => t.Id);
            transfer.Property(t => t.Amount).HasPrecision(18, 2);
            transfer.Property(t => t.Currency).HasMaxLength(3).IsRequired();
            transfer.Property(t => t.Description).HasMaxLength(500);
            transfer.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
            transfer.HasIndex(t => t.SourceAccountId);
            transfer.HasIndex(t => t.DestinationAccountId);
        });

        modelBuilder.Entity<LedgerEntry>(entry =>
        {
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Amount).HasPrecision(18, 2);
            entry.Property(e => e.BalanceAfter).HasPrecision(18, 2);
            entry.Property(e => e.Direction).HasConversion<string>().HasMaxLength(10);
            entry.HasIndex(e => new { e.AccountId, e.CreatedAt });
            entry.HasIndex(e => e.TransferId);
        });

        modelBuilder.Entity<OutboxMessage>(message =>
        {
            message.HasKey(m => m.Id);
            message.Property(m => m.Type).HasMaxLength(200).IsRequired();
            message.Property(m => m.Payload).IsRequired();
            message.HasIndex(m => m.ProcessedAt);
        });

        modelBuilder.Entity<IdempotencyRecord>(record =>
        {
            record.HasKey(r => r.Id);
            record.Property(r => r.Key).HasMaxLength(200).IsRequired();
            record.Property(r => r.RequestHash).HasMaxLength(64).IsRequired();
            record.Ignore(r => r.IsCompleted);
            record.HasIndex(r => new { r.UserId, r.Key }).IsUnique();
        });
    }
}
