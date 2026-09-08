using Meridian.Infrastructure.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meridian.Tests.Infrastructure;

public sealed class RabbitMqEventPublisherTests
{
    [Fact]
    public void TryPublish_WithAnUnreachableBroker_ReturnsFalse()
    {
        using var publisher = new RabbitMqEventPublisher(
            Options.Create(new RabbitMqOptions
            {
                HostName = "127.0.0.1",
                Port = 1,
                ConfirmTimeoutSeconds = 1,
            }),
            NullLogger<RabbitMqEventPublisher>.Instance);

        var published = publisher.TryPublish("TransferCompleted", """{"transferId":"1"}""");

        Assert.False(published);
    }
}
