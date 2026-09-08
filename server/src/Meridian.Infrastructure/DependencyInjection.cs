using Meridian.Application.Abstractions;
using Meridian.Application.Services;
using Meridian.Infrastructure.Messaging;
using Meridian.Infrastructure.Persistence;
using Meridian.Infrastructure.Persistence.Repositories;
using Meridian.Infrastructure.Security;
using Meridian.Infrastructure.Time;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure;

public static class DependencyInjection
{
    public const string SqliteProviderName = "Sqlite";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        AddPersistence(services, configuration);

        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ILedgerEntryRepository, LedgerEntryRepository>();
        services.AddScoped<ITransferRepository, TransferRepository>();
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();

        services.AddScoped<ISystemAccountProvider, SystemAccountProvider>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ITransferService, TransferService>();

        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
        services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();

        if (configuration.GetValue($"{OutboxOptions.SectionName}:Enabled", defaultValue: true))
            services.AddHostedService<OutboxProcessor>();

        return services;
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        if (UsesSqlite(configuration))
        {
            var connectionString = configuration.GetConnectionString(SqliteProviderName) ?? "DataSource=:memory:";
            var sharedCacheConnectionString = ToNamedSharedCacheConnectionString(connectionString);

            // Keeps the named in-memory database alive for the app's lifetime: SQLite drops a
            // Mode=Memory database once its last connection closes, so this singleton connection
            // is never used for queries, only held open. Every scope below opens its own
            // connection against the same shared-cache name, so requests don't share one
            // SqliteConnection instance (see MER-001 achado 7 / D10).
            services.AddSingleton(_ =>
            {
                var keepAlive = new SqliteConnection(sharedCacheConnectionString);
                keepAlive.Open();
                return keepAlive;
            });

            services.AddDbContext<MeridianDbContext>((provider, options) =>
            {
                _ = provider.GetRequiredService<SqliteConnection>();
                options.UseSqlite(sharedCacheConnectionString);
            });
        }
        else
        {
            var connectionString = configuration.GetConnectionString("Default")
                ?? "Host=localhost;Port=5432;Database=meridian;Username=meridian;Password=meridian";
            services.AddDbContext<MeridianDbContext>(options => options.UseNpgsql(connectionString));
        }
    }

    private static string ToNamedSharedCacheConnectionString(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (string.IsNullOrEmpty(builder.DataSource) || builder.DataSource == ":memory:")
        {
            builder.DataSource = $"meridian-{Guid.NewGuid():N}";
            builder.Mode = SqliteOpenMode.Memory;
        }

        builder.Cache = SqliteCacheMode.Shared;
        return builder.ToString();
    }

    public static bool UsesSqlite(IConfiguration configuration) =>
        string.Equals(configuration["Database:Provider"], SqliteProviderName, StringComparison.OrdinalIgnoreCase);

    public static void EnsureDatabase(IServiceProvider services, IConfiguration configuration, ILogger logger)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MeridianDbContext>();

        if (UsesSqlite(configuration))
        {
            context.Database.EnsureCreated();
            return;
        }

        try
        {
            context.Database.Migrate();
        }
        catch (Exception ex)
        {
            logger.LogError(
                "Could not apply migrations — is PostgreSQL up? The API will start anyway. ({Error})", ex.Message);
        }
    }
}
