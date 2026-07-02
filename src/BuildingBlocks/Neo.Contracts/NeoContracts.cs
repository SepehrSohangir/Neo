using System.Text.Json;
using System.Text.Json.Serialization;

namespace Neo.Contracts;

public static class ApiRoutes
{
    public const string AuthLogin = "/auth/login";
    public const string Stores = "/stores";
    public const string SyncSnapshot = "/sync/snapshot";
    public const string SyncChanges = "/sync/changes";
    public const string SyncEvents = "/sync/events";
    public const string SyncAck = "/sync/ack";
    public const string Invoices = "/invoices";
    public const string Products = "/products";
    public const string Prices = "/prices";
}

public static class AuthDefaults
{
    public const string Issuer = "neo-auth";
    public const string Audience = "neo-mobile";
    public const string SigningKey = "neo-mvp-super-secret-key-123456789";
}

public enum SyncState
{
    Pending = 0,
    Synced = 1,
    Failed = 2,
    Review = 3
}

public enum InvoiceLifecycleStatus
{
    Draft = 0,
    Submitted = 1,
    Voided = 2
}

public enum InvoiceEventType
{
    InvoiceCreated = 1,
    InvoiceUpdated = 2,
    InvoiceVoided = 3,
    PriceUpdated = 4,
    StoreSyncChanged = 5
}

public sealed record LoginRequest(string Username, string Password);

public sealed record AuthStoreDto(Guid StoreId, string StoreCode, string StoreName);

public sealed record LoginResponse(
    string AccessToken,
    Guid UserId,
    string DisplayName,
    IReadOnlyList<AuthStoreDto> Stores,
    string DefaultBaseUrl);

public sealed record InvoiceItemDto(
    Guid InvoiceItemId,
    Guid ProductId,
    string ProductName,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public sealed record InvoiceDto(
    Guid InvoiceId,
    Guid StoreId,
    Guid DeviceId,
    Guid UserId,
    string InvoiceNumber,
    InvoiceLifecycleStatus Status,
    long ServerVersion,
    long? ServerSequence,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    SyncState SyncState,
    IReadOnlyList<InvoiceItemDto> Items);

public sealed record ProductPriceDto(
    Guid ProductId,
    Guid StoreId,
    string Sku,
    string Name,
    decimal UnitPrice,
    string Currency,
    DateTimeOffset UpdatedAt);

public sealed record StoreSnapshotResponse(
    Guid StoreId,
    long SnapshotCursor,
    IReadOnlyList<InvoiceDto> Invoices,
    IReadOnlyList<ProductPriceDto> Products);

public sealed record SyncEnvelopeDto(
    Guid EventId,
    Guid StoreId,
    Guid DeviceId,
    Guid UserId,
    Guid AggregateId,
    InvoiceEventType EventType,
    string IdempotencyKey,
    long ClientLocalVersion,
    long? ClientBaseServerVersion,
    DateTimeOffset OccurredAt,
    JsonElement Payload);

public sealed record SyncUploadRequest(
    Guid StoreId,
    Guid DeviceId,
    IReadOnlyList<SyncEnvelopeDto> Events);

public sealed record SyncUploadResultDto(
    Guid EventId,
    bool Accepted,
    long? ServerSequence,
    long? ServerVersion,
    SyncState ResultingState,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record SyncUploadResponse(
    Guid StoreId,
    IReadOnlyList<SyncUploadResultDto> Results);

public sealed record ChangeFeedItemDto(
    long ServerSequence,
    Guid EventId,
    Guid StoreId,
    Guid AggregateId,
    InvoiceEventType EventType,
    DateTimeOffset OccurredAt,
    JsonElement Payload);

public sealed record IncrementalChangesResponse(
    Guid StoreId,
    long FromCursor,
    long ToCursor,
    IReadOnlyList<ChangeFeedItemDto> Changes);

public sealed record SyncAckRequest(Guid StoreId, Guid DeviceId, long LastAppliedSequence);

public sealed record StoreSelectionRequest(Guid StoreId);

public sealed record CreateInvoiceRequest(
    Guid StoreId,
    Guid DeviceId,
    Guid UserId,
    string InvoiceNumber,
    IReadOnlyList<CreateInvoiceItemRequest> Items);

public sealed record CreateInvoiceItemRequest(
    Guid ProductId,
    string ProductName,
    decimal Quantity,
    decimal UnitPrice);

public static class DemoIdentity
{
    public static readonly Guid DefaultStoreId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid DemoUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid ProductAppleId = Guid.Parse("33333333-3333-3333-3333-333333333331");
    public static readonly Guid ProductMilkId = Guid.Parse("33333333-3333-3333-3333-333333333332");
}
