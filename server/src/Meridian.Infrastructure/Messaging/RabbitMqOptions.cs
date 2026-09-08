namespace Meridian.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; init; } = "localhost";
    public int Port { get; init; } = 5672;
    public string UserName { get; init; } = "guest";
    public string Password { get; init; } = "guest";
    public string Exchange { get; init; } = "meridian.events";
    public int ConfirmTimeoutSeconds { get; init; } = 5;
}

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public bool Enabled { get; init; } = true;
    public int PollIntervalSeconds { get; init; } = 2;
    public int BatchSize { get; init; } = 20;
}
