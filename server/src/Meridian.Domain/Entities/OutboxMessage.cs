namespace Meridian.Domain.Entities;

public class OutboxMessage
{
    public Guid Id { get; private set; }
    public string Type { get; private set; } = null!;
    public string Payload { get; private set; } = null!;
    public DateTime OccurredAt { get; private set; }
    public DateTime? ProcessedAt { get; private set; }
    public int Attempts { get; private set; }

    private OutboxMessage() { }

    public static OutboxMessage Create(string type, string payload, DateTime occurredAt) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        Payload = payload,
        OccurredAt = occurredAt,
        Attempts = 0,
    };

    public void MarkProcessed(DateTime processedAt) => ProcessedAt = processedAt;

    public void RegisterAttempt() => Attempts++;
}
