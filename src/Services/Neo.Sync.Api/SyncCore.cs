using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Neo.Contracts;
using Neo.Eventing;

namespace Neo.Sync.Api;

public sealed class SyncDbContext(DbContextOptions<SyncDbContext> options) : DbContext(options)
{
    public DbSet<StoreRecord> Stores => Set<StoreRecord>();
    public DbSet<ProductPriceRecord> ProductPrices => Set<ProductPriceRecord>();
    public DbSet<InvoiceRecord> Invoices => Set<InvoiceRecord>();
    public DbSet<InvoiceItemRecord> InvoiceItems => Set<InvoiceItemRecord>();
    public DbSet<SyncInboxRecord> SyncInbox => Set<SyncInboxRecord>();
    public DbSet<StoreChangeRecord> StoreChanges => Set<StoreChangeRecord>();
    public DbSet<DeviceCheckpointRecord> DeviceCheckpoints => Set<DeviceCheckpointRecord>();
    public DbSet<SyncConflictRecord> SyncConflicts => Set<SyncConflictRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoreRecord>().HasKey(x => x.StoreId);

        modelBuilder.Entity<ProductPriceRecord>()
            .HasIndex(x => new { x.StoreId, x.ProductId })
            .IsUnique();

        modelBuilder.Entity<InvoiceRecord>()
            .HasKey(x => x.InvoiceId);

        modelBuilder.Entity<InvoiceRecord>()
            .HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey(x => x.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<InvoiceRecord>()
            .HasIndex(x => new { x.StoreId, x.InvoiceId })
            .IsUnique();

        modelBuilder.Entity<InvoiceItemRecord>().HasKey(x => x.InvoiceItemId);

        modelBuilder.Entity<SyncInboxRecord>().HasKey(x => x.EventId);
        modelBuilder.Entity<SyncInboxRecord>()
            .HasIndex(x => x.IdempotencyKey)
            .IsUnique();

        modelBuilder.Entity<StoreChangeRecord>().HasKey(x => x.ChangeId);
        modelBuilder.Entity<StoreChangeRecord>()
            .HasIndex(x => new { x.StoreId, x.ServerSequence })
            .IsUnique();

        modelBuilder.Entity<DeviceCheckpointRecord>()
            .HasKey(x => new { x.StoreId, x.DeviceId });

        modelBuilder.Entity<SyncConflictRecord>().HasKey(x => x.ConflictId);
    }
}

public sealed class StoreRecord
{
    [Key]
    public Guid StoreId { get; set; }
    public string StoreCode { get; set; } = string.Empty;
    public string StoreName { get; set; } = string.Empty;
    public long CurrentSequence { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ProductPriceRecord
{
    [Key]
    public Guid ProductId { get; set; }
    public Guid StoreId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class InvoiceRecord
{
    [Key]
    public Guid InvoiceId { get; set; }
    public Guid StoreId { get; set; }
    public Guid DeviceId { get; set; }
    public Guid UserId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public InvoiceLifecycleStatus Status { get; set; }
    public long ServerVersion { get; set; }
    public long? ServerSequence { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string ConflictState { get; set; } = "None";
    public List<InvoiceItemRecord> Items { get; set; } = [];
}

public sealed class InvoiceItemRecord
{
    [Key]
    public Guid InvoiceItemId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class SyncInboxRecord
{
    [Key]
    public Guid EventId { get; set; }
    public Guid StoreId { get; set; }
    public Guid DeviceId { get; set; }
    public Guid AggregateId { get; set; }
    public InvoiceEventType EventType { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public bool Accepted { get; set; }
    public long? ServerSequence { get; set; }
    public long? ServerVersion { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}

public sealed class StoreChangeRecord
{
    [Key]
    public Guid ChangeId { get; set; }
    public Guid StoreId { get; set; }
    public long ServerSequence { get; set; }
    public Guid EventId { get; set; }
    public Guid AggregateId { get; set; }
    public InvoiceEventType EventType { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
}

public sealed class DeviceCheckpointRecord
{
    public Guid StoreId { get; set; }
    public Guid DeviceId { get; set; }
    public long LastAckedSequence { get; set; }
    public DateTimeOffset? LastPullAt { get; set; }
    public DateTimeOffset? LastPushAt { get; set; }
}

public sealed class SyncConflictRecord
{
    [Key]
    public Guid ConflictId { get; set; }
    public Guid StoreId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid LosingEventId { get; set; }
    public Guid? WinningEventId { get; set; }
    public Guid DeviceId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed record InvoicePayload(string InvoiceNumber, InvoiceLifecycleStatus Status, IReadOnlyList<InvoiceItemDto> Items);

public sealed class SyncOrchestrator(SyncDbContext dbContext, IIntegrationEventPublisher publisher)
{
    public async Task EnsureSeedDataAsync(CancellationToken cancellationToken = default)
    {
        if (await dbContext.Stores.AnyAsync(cancellationToken))
        {
            return;
        }

        var storeId = DemoIdentity.DefaultStoreId;
        dbContext.Stores.Add(new StoreRecord
        {
            StoreId = storeId,
            StoreCode = "STORE-001",
            StoreName = "Downtown Market",
            CurrentSequence = 0,
            CreatedAt = DateTimeOffset.UtcNow
        });

        dbContext.ProductPrices.AddRange(
            new ProductPriceRecord
            {
                ProductId = DemoIdentity.ProductAppleId,
                StoreId = storeId,
                Sku = "APL-001",
                Name = "Apple",
                UnitPrice = 2.50m,
                Currency = "USD",
                UpdatedAt = DateTimeOffset.UtcNow
            },
            new ProductPriceRecord
            {
                ProductId = DemoIdentity.ProductMilkId,
                StoreId = storeId,
                Sku = "MLK-001",
                Name = "Milk",
                UnitPrice = 4.75m,
                Currency = "USD",
                UpdatedAt = DateTimeOffset.UtcNow
            });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<StoreSnapshotResponse> GetSnapshotAsync(Guid storeId, CancellationToken cancellationToken = default)
    {
        var store = await dbContext.Stores.SingleAsync(x => x.StoreId == storeId, cancellationToken);
        var invoices = await dbContext.Invoices
            .Include(x => x.Items)
            .Where(x => x.StoreId == storeId)
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(cancellationToken);

        var products = await dbContext.ProductPrices
            .Where(x => x.StoreId == storeId)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return new StoreSnapshotResponse(
            storeId,
            store.CurrentSequence,
            invoices.Select(ToDto).ToList(),
            products.Select(x => new ProductPriceDto(x.ProductId, x.StoreId, x.Sku, x.Name, x.UnitPrice, x.Currency, x.UpdatedAt)).ToList());
    }

    public async Task<IncrementalChangesResponse> GetChangesAsync(Guid storeId, long sinceCursor, int limit, Guid? deviceId, CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.StoreChanges
            .Where(x => x.StoreId == storeId && x.ServerSequence > sinceCursor)
            .OrderBy(x => x.ServerSequence)
            .Take(limit)
            .ToListAsync(cancellationToken);

        if (deviceId.HasValue)
        {
            var ownEventIds = await dbContext.SyncInbox
                .Where(x => x.StoreId == storeId && x.DeviceId == deviceId.Value && x.Accepted)
                .Select(x => x.EventId)
                .ToListAsync(cancellationToken);

            if (ownEventIds.Count > 0)
            {
                var ownEventIdSet = ownEventIds.ToHashSet();
                rows = rows.Where(x => !ownEventIdSet.Contains(x.EventId)).ToList();
            }

            var checkpoint = await dbContext.DeviceCheckpoints.FindAsync([storeId, deviceId.Value], cancellationToken);
            if (checkpoint is null)
            {
                checkpoint = new DeviceCheckpointRecord { StoreId = storeId, DeviceId = deviceId.Value };
                dbContext.DeviceCheckpoints.Add(checkpoint);
            }

            checkpoint.LastPullAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var changes = rows.Select(x => new ChangeFeedItemDto(
            x.ServerSequence,
            x.EventId,
            x.StoreId,
            x.AggregateId,
            x.EventType,
            x.OccurredAt,
            JsonSerializer.Deserialize<JsonElement>(x.PayloadJson))).ToList();

        var toCursor = rows.Count == 0 ? sinceCursor : rows[^1].ServerSequence;
        return new IncrementalChangesResponse(storeId, sinceCursor, toCursor, changes);
    }

    public async Task<SyncUploadResponse> ProcessUploadAsync(SyncUploadRequest request, CancellationToken cancellationToken = default)
    {
        var results = new List<SyncUploadResultDto>(request.Events.Count);

        foreach (var envelope in request.Events.OrderBy(x => x.ClientLocalVersion))
        {
            var existing = await dbContext.SyncInbox.FindAsync([envelope.EventId], cancellationToken);
            if (existing is not null)
            {
                results.Add(new SyncUploadResultDto(
                    existing.EventId,
                    existing.Accepted,
                    existing.ServerSequence,
                    existing.ServerVersion,
                    existing.Accepted ? SyncState.Synced : SyncState.Failed,
                    existing.ErrorCode,
                    existing.ErrorMessage));
                continue;
            }

            var duplicateKey = await dbContext.SyncInbox.SingleOrDefaultAsync(x => x.IdempotencyKey == envelope.IdempotencyKey, cancellationToken);
            if (duplicateKey is not null)
            {
                results.Add(new SyncUploadResultDto(
                    envelope.EventId,
                    duplicateKey.Accepted,
                    duplicateKey.ServerSequence,
                    duplicateKey.ServerVersion,
                    duplicateKey.Accepted ? SyncState.Synced : SyncState.Failed,
                    duplicateKey.ErrorCode,
                    "Duplicate idempotency key."));
                continue;
            }

            var result = await AcceptNewEnvelopeAsync(envelope, cancellationToken);
            results.Add(result);
        }

        return new SyncUploadResponse(request.StoreId, results);
    }

    public async Task AcknowledgeAsync(SyncAckRequest request, CancellationToken cancellationToken = default)
    {
        var checkpoint = await dbContext.DeviceCheckpoints.FindAsync([request.StoreId, request.DeviceId], cancellationToken);
        if (checkpoint is null)
        {
            checkpoint = new DeviceCheckpointRecord
            {
                StoreId = request.StoreId,
                DeviceId = request.DeviceId
            };
            dbContext.DeviceCheckpoints.Add(checkpoint);
        }

        checkpoint.LastAckedSequence = Math.Max(checkpoint.LastAckedSequence, request.LastAppliedSequence);
        checkpoint.LastPushAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<SyncUploadResultDto> AcceptNewEnvelopeAsync(SyncEnvelopeDto envelope, CancellationToken cancellationToken)
    {
        var inbox = new SyncInboxRecord
        {
            EventId = envelope.EventId,
            StoreId = envelope.StoreId,
            DeviceId = envelope.DeviceId,
            AggregateId = envelope.AggregateId,
            EventType = envelope.EventType,
            IdempotencyKey = envelope.IdempotencyKey,
            ReceivedAt = DateTimeOffset.UtcNow
        };

        dbContext.SyncInbox.Add(inbox);

        try
        {
            var store = await dbContext.Stores.SingleAsync(x => x.StoreId == envelope.StoreId, cancellationToken);
            var invoice = await dbContext.Invoices
                .Include(x => x.Items)
                .SingleOrDefaultAsync(x => x.InvoiceId == envelope.AggregateId && x.StoreId == envelope.StoreId, cancellationToken);

            if (envelope.EventType == InvoiceEventType.InvoiceCreated && invoice is not null)
            {
                return await RejectAsync(inbox, "duplicate_invoice", "Invoice already exists.", cancellationToken);
            }

            if (envelope.EventType != InvoiceEventType.InvoiceCreated && invoice is null)
            {
                return await RejectAsync(inbox, "invoice_not_found", "Invoice does not exist on the server.", cancellationToken);
            }

            if (invoice is not null
                && envelope.ClientBaseServerVersion.HasValue
                && envelope.ClientBaseServerVersion.Value != invoice.ServerVersion)
            {
                return await RejectAsync(inbox, "conflict", "Invoice was changed on another device and requires review.", cancellationToken, SyncState.Review);
            }

            var payload = envelope.Payload.Deserialize<InvoicePayload>(SerializerOptions.Default)
                ?? throw new InvalidOperationException("Event payload is invalid.");

            if (envelope.EventType == InvoiceEventType.InvoiceUpdated && invoice is not null)
            {
                dbContext.InvoiceItems.RemoveRange(invoice.Items);
                invoice.Items.Clear();
            }

            invoice = envelope.EventType switch
            {
                InvoiceEventType.InvoiceCreated => CreateInvoice(envelope, payload),
                InvoiceEventType.InvoiceUpdated => UpdateInvoice(invoice!, envelope, payload),
                InvoiceEventType.InvoiceVoided => VoidInvoice(invoice!, envelope),
                _ => throw new InvalidOperationException($"Unsupported event type {envelope.EventType}.")
            };

            if (envelope.EventType == InvoiceEventType.InvoiceCreated)
            {
                dbContext.Invoices.Add(invoice);
            }

            store.CurrentSequence++;
            invoice.ServerSequence = store.CurrentSequence;
            invoice.ServerVersion++;
            invoice.UpdatedAt = DateTimeOffset.UtcNow;

            var invoiceDto = ToDto(invoice);
            var payloadElement = JsonSerializer.SerializeToElement(invoiceDto, SerializerOptions.Default);

            dbContext.StoreChanges.Add(new StoreChangeRecord
            {
                ChangeId = Guid.NewGuid(),
                StoreId = envelope.StoreId,
                ServerSequence = store.CurrentSequence,
                EventId = envelope.EventId,
                AggregateId = invoice.InvoiceId,
                EventType = envelope.EventType,
                OccurredAt = DateTimeOffset.UtcNow,
                PayloadJson = payloadElement.GetRawText()
            });

            inbox.Accepted = true;
            inbox.ServerSequence = store.CurrentSequence;
            inbox.ServerVersion = invoice.ServerVersion;

            await dbContext.SaveChangesAsync(cancellationToken);
            await publisher.PublishAsync("invoice-events", envelope.StoreId.ToString("N"), envelope, cancellationToken);

            return new SyncUploadResultDto(
                envelope.EventId,
                true,
                store.CurrentSequence,
                invoice.ServerVersion,
                SyncState.Synced,
                null,
                null);
        }
        catch (Exception ex)
        {
            return await RejectAsync(inbox, "server_error", ex.Message, cancellationToken);
        }
    }

    private async Task<SyncUploadResultDto> RejectAsync(
        SyncInboxRecord inbox,
        string code,
        string message,
        CancellationToken cancellationToken,
        SyncState state = SyncState.Failed)
    {
        inbox.Accepted = false;
        inbox.ErrorCode = code;
        inbox.ErrorMessage = message;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new SyncUploadResultDto(
            inbox.EventId,
            false,
            null,
            inbox.ServerVersion,
            state,
            code,
            message);
    }

    private static InvoiceRecord CreateInvoice(SyncEnvelopeDto envelope, InvoicePayload payload)
    {
        return new InvoiceRecord
        {
            InvoiceId = envelope.AggregateId,
            StoreId = envelope.StoreId,
            DeviceId = envelope.DeviceId,
            UserId = envelope.UserId,
            InvoiceNumber = payload.InvoiceNumber,
            Status = payload.Status,
            ServerVersion = 0,
            CreatedAt = envelope.OccurredAt,
            UpdatedAt = envelope.OccurredAt,
            Items = payload.Items.Select(x => ToEntity(envelope.AggregateId, x)).ToList()
        };
    }

    private static InvoiceRecord UpdateInvoice(InvoiceRecord invoice, SyncEnvelopeDto envelope, InvoicePayload payload)
    {
        invoice.DeviceId = envelope.DeviceId;
        invoice.UserId = envelope.UserId;
        invoice.InvoiceNumber = payload.InvoiceNumber;
        invoice.Status = payload.Status;
        invoice.Items.Clear();
        invoice.Items.AddRange(payload.Items.Select(x => ToEntity(invoice.InvoiceId, x)));
        return invoice;
    }

    private static InvoiceRecord VoidInvoice(InvoiceRecord invoice, SyncEnvelopeDto envelope)
    {
        invoice.DeviceId = envelope.DeviceId;
        invoice.UserId = envelope.UserId;
        invoice.Status = InvoiceLifecycleStatus.Voided;
        return invoice;
    }

    private static InvoiceItemRecord ToEntity(Guid invoiceId, InvoiceItemDto dto)
    {
        return new InvoiceItemRecord
        {
            InvoiceItemId = dto.InvoiceItemId,
            InvoiceId = invoiceId,
            ProductId = dto.ProductId,
            ProductName = dto.ProductName,
            Quantity = dto.Quantity,
            UnitPrice = dto.UnitPrice,
            LineTotal = dto.LineTotal
        };
    }

    public static InvoiceDto ToDto(InvoiceRecord invoice)
    {
        return new InvoiceDto(
            invoice.InvoiceId,
            invoice.StoreId,
            invoice.DeviceId,
            invoice.UserId,
            invoice.InvoiceNumber,
            invoice.Status,
            invoice.ServerVersion,
            invoice.ServerSequence,
            invoice.CreatedAt,
            invoice.UpdatedAt,
            SyncState.Synced,
            invoice.Items
                .OrderBy(x => x.ProductName)
                .Select(x => new InvoiceItemDto(
                    x.InvoiceItemId,
                    x.ProductId,
                    x.ProductName,
                    x.Quantity,
                    x.UnitPrice,
                    x.LineTotal))
                .ToList());
    }
}

public static class SerializerOptions
{
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}

