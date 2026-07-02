using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Neo.Contracts;
using Neo.Eventing;
using Neo.Sync.Api;

namespace Neo.Sync.Tests;

public class SyncOrchestratorTests
{
    [Fact]
    public async Task Duplicate_event_id_is_idempotent()
    {
        var dbName = nameof(Duplicate_event_id_is_idempotent);
        await SeedAsync(dbName);

        var request = CreateUploadRequest(eventId: Guid.NewGuid(), baseVersion: 0);

        var first = await ExecuteAsync(dbName, orchestrator => orchestrator.ProcessUploadAsync(request));
        var second = await ExecuteAsync(dbName, orchestrator => orchestrator.ProcessUploadAsync(request));

        Assert.Single(first.Results);
        Assert.Single(second.Results);
        Assert.True(first.Results[0].Accepted);
        Assert.True(second.Results[0].Accepted);
        Assert.Equal(first.Results[0].ServerSequence, second.Results[0].ServerSequence);
    }

    [Fact]
    public async Task Accepted_events_advance_change_cursor()
    {
        var dbName = nameof(Accepted_events_advance_change_cursor);
        await SeedAsync(dbName);

        var invoiceId = Guid.NewGuid();
        var secondInvoiceId = Guid.NewGuid();

        var createResponse = await ExecuteAsync(dbName, orchestrator => orchestrator.ProcessUploadAsync(CreateUploadRequest(Guid.NewGuid(), 0, invoiceId)));
        Assert.True(createResponse.Results[0].Accepted);

        var secondCreate = await ExecuteAsync(dbName, orchestrator => orchestrator.ProcessUploadAsync(CreateUploadRequest(Guid.NewGuid(), 0, secondInvoiceId)));
        Assert.True(secondCreate.Results[0].Accepted);

        var changes = await ExecuteAsync(dbName, orchestrator => orchestrator.GetChangesAsync(DemoIdentity.DefaultStoreId, 0, 10, Guid.NewGuid()));
        Assert.Equal(2, changes.Changes.Count);
        Assert.Equal(2, changes.ToCursor);
    }

    [Fact]
    public async Task GetChanges_excludes_events_originated_by_requesting_device()
    {
        var dbName = nameof(GetChanges_excludes_events_originated_by_requesting_device);
        await SeedAsync(dbName);

        var deviceId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        var upload = new SyncUploadRequest(
            DemoIdentity.DefaultStoreId,
            deviceId,
            [
                new SyncEnvelopeDto(
                    eventId,
                    DemoIdentity.DefaultStoreId,
                    deviceId,
                    DemoIdentity.DemoUserId,
                    invoiceId,
                    InvoiceEventType.InvoiceCreated,
                    $"{invoiceId:N}:{eventId:N}",
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    0,
                    DateTimeOffset.UtcNow,
                    JsonSerializer.SerializeToElement(new
                    {
                        invoiceNumber = "INV-ECHO-TEST",
                        status = InvoiceLifecycleStatus.Submitted,
                        items = new[]
                        {
                            new InvoiceItemDto(Guid.NewGuid(), DemoIdentity.ProductAppleId, "Apple", 1, 2.50m, 2.50m)
                        }
                    }, SerializerOptions.Default))
            ]);

        var uploadResponse = await ExecuteAsync(dbName, orchestrator => orchestrator.ProcessUploadAsync(upload));
        Assert.True(uploadResponse.Results[0].Accepted);

        var ownChanges = await ExecuteAsync(dbName, orchestrator => orchestrator.GetChangesAsync(DemoIdentity.DefaultStoreId, 0, 10, deviceId));
        Assert.Empty(ownChanges.Changes);

        var otherDeviceChanges = await ExecuteAsync(dbName, orchestrator => orchestrator.GetChangesAsync(DemoIdentity.DefaultStoreId, 0, 10, Guid.NewGuid()));
        Assert.Single(otherDeviceChanges.Changes);
        Assert.Equal(eventId, otherDeviceChanges.Changes[0].EventId);
    }

    private static async Task SeedAsync(string dbName)
    {
        await ExecuteAsync(dbName, orchestrator => orchestrator.EnsureSeedDataAsync());
    }

    private static async Task<T> ExecuteAsync<T>(string dbName, Func<SyncOrchestrator, Task<T>> action)
    {
        await using var db = CreateDb(dbName);
        var orchestrator = new SyncOrchestrator(db, new LoggingKafkaEventPublisher());
        return await action(orchestrator);
    }

    private static async Task ExecuteAsync(string dbName, Func<SyncOrchestrator, Task> action)
    {
        await using var db = CreateDb(dbName);
        var orchestrator = new SyncOrchestrator(db, new LoggingKafkaEventPublisher());
        await action(orchestrator);
    }

    private static SyncDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<SyncDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        return new SyncDbContext(options);
    }

    private static SyncUploadRequest CreateUploadRequest(Guid eventId, long baseVersion, Guid? invoiceId = null, InvoiceEventType eventType = InvoiceEventType.InvoiceCreated)
    {
        var aggregateId = invoiceId ?? Guid.NewGuid();
        var payload = JsonSerializer.SerializeToElement(new
        {
            invoiceNumber = "INV-TEST-001",
            status = InvoiceLifecycleStatus.Submitted,
            items = new[]
            {
                new InvoiceItemDto(Guid.NewGuid(), DemoIdentity.ProductAppleId, "Apple", 2, 2.50m, 5.00m)
            }
        }, SerializerOptions.Default);

        return new SyncUploadRequest(
            DemoIdentity.DefaultStoreId,
            Guid.NewGuid(),
            [
                new SyncEnvelopeDto(
                    eventId,
                    DemoIdentity.DefaultStoreId,
                    Guid.NewGuid(),
                    DemoIdentity.DemoUserId,
                    aggregateId,
                    eventType,
                    $"{aggregateId:N}:{eventId:N}",
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    baseVersion,
                    DateTimeOffset.UtcNow,
                    payload)
            ]);
    }
}
