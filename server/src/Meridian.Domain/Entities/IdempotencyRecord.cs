namespace Meridian.Domain.Entities;

public class IdempotencyRecord
{
    public const int MaxKeyLength = 200;

    public Guid Id { get; private set; }
    public string Key { get; private set; } = null!;
    public Guid UserId { get; private set; }
    public string RequestHash { get; private set; } = null!;
    public int? ResponseStatusCode { get; private set; }
    public string? ResponseBody { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    public bool IsCompleted => CompletedAt is not null;

    private IdempotencyRecord() { }

    public static IdempotencyRecord Reserve(
        string key,
        Guid userId,
        string requestHash,
        DateTime createdAt) => new()
        {
            Id = Guid.NewGuid(),
            Key = key,
            UserId = userId,
            RequestHash = requestHash,
            CreatedAt = createdAt,
        };

    public void Complete(int responseStatusCode, string responseBody, DateTime completedAt)
    {
        ResponseStatusCode = responseStatusCode;
        ResponseBody = responseBody;
        CompletedAt = completedAt;
    }
}
