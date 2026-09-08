namespace Meridian.Domain.Entities;

public class IdempotencyRecord
{
    public Guid Id { get; private set; }
    public string Key { get; private set; } = null!;
    public Guid UserId { get; private set; }
    public string RequestHash { get; private set; } = null!;
    public int ResponseStatusCode { get; private set; }
    public string ResponseBody { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }

    private IdempotencyRecord() { }

    public static IdempotencyRecord Create(
        string key,
        Guid userId,
        string requestHash,
        int responseStatusCode,
        string responseBody,
        DateTime createdAt) => new()
        {
            Id = Guid.NewGuid(),
            Key = key,
            UserId = userId,
            RequestHash = requestHash,
            ResponseStatusCode = responseStatusCode,
            ResponseBody = responseBody,
            CreatedAt = createdAt,
        };
}
