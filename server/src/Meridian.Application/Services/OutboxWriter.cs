using System.Text.Json;
using Meridian.Application.Abstractions;
using Meridian.Domain.Entities;
using Meridian.Domain.Events;

namespace Meridian.Application.Services;

public static class OutboxWriter
{
    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static void Enqueue(IOutboxRepository outbox, TransferCompleted @event)
    {
        var payload = JsonSerializer.Serialize(@event, SerializerOptions);
        outbox.Add(OutboxMessage.Create(nameof(TransferCompleted), payload, @event.OccurredAt));
    }
}
