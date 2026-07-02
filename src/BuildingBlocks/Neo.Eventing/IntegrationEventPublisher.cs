using Confluent.Kafka;
using Neo.Contracts;

namespace Neo.Eventing;

public interface IIntegrationEventPublisher
{
    Task PublishAsync(string topic, string key, SyncEnvelopeDto envelope, CancellationToken cancellationToken = default);
}

public sealed class LoggingKafkaEventPublisher : IIntegrationEventPublisher
{
    public Task PublishAsync(string topic, string key, SyncEnvelopeDto envelope, CancellationToken cancellationToken = default)
    {
        // MVP stub: keep the contract Kafka-ready while remaining runnable on one server.
        Console.WriteLine(
            "Publishing event to topic {0} with key {1}. EventId={2} StoreId={3} EventType={4}",
            topic,
            key,
            envelope.EventId,
            envelope.StoreId,
            envelope.EventType);

        return Task.CompletedTask;
    }
}
