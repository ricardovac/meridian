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
            services.AddSingleton(_ =>
            {
                var connection = new SqliteConnection(connectionString);
                connection.Open();
                return connection;
            });
            services.AddDbContext<MeridianDbContext>((provider, options) =>
                options.UseSqlite(provider.GetRequiredService<SqliteConnection>()));
        }
        else
        {
            var connectionString = configuration.GetConnectionString("Default")
                ?? "Host=localhost;Port=5432;Database=meridian;Username=meridian;Password=meridian";
            services.AddDbContext<MeridianDbContext>(options => options.UseNpgsql(connectionString));
        }
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
