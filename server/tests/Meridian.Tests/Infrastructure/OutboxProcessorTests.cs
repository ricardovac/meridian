using System.Collections.Concurrent;
using Meridian.Application.Abstractions;
using Meridian.Domain.Entities;
using Meridian.Infrastructure.Messaging;
using Meridian.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meridian.Tests.Infrastructure;

public sealed class OutboxProcessorTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<MeridianDbContext> _options;

    public OutboxProcessorTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<MeridianDbContext>().UseSqlite(_connection).Options;

        using var context = new MeridianDbContext(_options);
        context.Database.EnsureCreated();
    }

    [Fact]
    public async Task UnpublishedMessage_StaysPending_AndRegistersAnAttempt()
    {
        var messageId = await SeedMessageAsync();
        var publisher = new StubPublisher(published: false);

        await RunOneCycleAsync(publisher, message => message.Attempts == 1);

        await using var context = new MeridianDbContext(_options);
        var message = await context.OutboxMessages.SingleAsync(m => m.Id == messageId);
        Assert.Null(message.ProcessedAt);
        Assert.Equal(1, message.Attempts);
        Assert.Equal("TransferCompleted", Assert.Single(publisher.Published).Type);
    }

    [Fact]
    public async Task PublishedMessage_IsMarkedProcessed()
    {
        var messageId = await SeedMessageAsync();
        var publisher = new StubPublisher(published: true);

        await RunOneCycleAsync(publisher, message => message.ProcessedAt is not null);

        await using var context = new MeridianDbContext(_options);
        var message = await context.OutboxMessages.SingleAsync(m => m.Id == messageId);
        Assert.NotNull(message.ProcessedAt);
        Assert.Equal(0, message.Attempts);
    }

    private async Task<Guid> SeedMessageAsync()
    {
        await using var context = new MeridianDbContext(_options);
        var message = OutboxMessage.Create("TransferCompleted", """{"transferId":"1"}""", Now);
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();
        return message.Id;
    }

    private async Task RunOneCycleAsync(IEventPublisher publisher, Func<OutboxMessage, bool> settled)
    {
        var services = new ServiceCollection();
        services.AddDbContext<MeridianDbContext>(options => options.UseSqlite(_connection));
        services.AddSingleton<IClock>(new FixedClock());
        await using var provider = services.BuildServiceProvider();

        var processor = new OutboxProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            publisher,
            Options.Create(new OutboxOptions { PollIntervalSeconds = 60, BatchSize = 20 }),
            NullLogger<OutboxProcessor>.Instance);

        await processor.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                await using var context = new MeridianDbContext(_options);
                var message = await context.OutboxMessages.SingleAsync();
                if (settled(message))
                    return;

                await Task.Delay(25);
            }

            Assert.Fail("The outbox processor did not settle the pending message in time.");
        }
        finally
        {
            await processor.StopAsync(CancellationToken.None);
        }
    }

    public void Dispose() => _connection.Dispose();

    private sealed class StubPublisher : IEventPublisher
    {
        private readonly bool _published;

        public StubPublisher(bool published) => _published = published;

        public ConcurrentQueue<(string Type, string Payload)> Published { get; } = new();

        public bool TryPublish(string eventType, string payload)
        {
            Published.Enqueue((eventType, payload));
            return _published;
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => Now;
    }
}
