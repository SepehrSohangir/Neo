using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Neo.Contracts;
using SQLite;

namespace Neo.Mobile.Maui;

public sealed class AppSession
{
    public string BaseUrl { get; set; } = "http://localhost:8080";
    public string AuthUrl { get; set; } = "http://localhost:8080";
    public string AccessToken { get; set; } = string.Empty;
    public Guid UserId { get; set; } = DemoIdentity.DemoUserId;
    public Guid DeviceId { get; } = Guid.NewGuid();
    public Guid StoreId { get; set; } = DemoIdentity.DefaultStoreId;
    public string Username { get; set; } = "demo";
    public string DisplayName { get; set; } = string.Empty;

    public bool IsLoggedIn => !string.IsNullOrWhiteSpace(AccessToken);

    public void Clear()
    {
        AccessToken = string.Empty;
        DisplayName = string.Empty;
        Username = string.Empty;
    }
}

public sealed class MobileRepository
{
    private readonly SQLiteAsyncConnection connection;

    public MobileRepository()
    {
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "neo-mobile.db3");
        connection = new SQLiteAsyncConnection(dbPath);
    }

    public async Task InitializeAsync()
    {
        await connection.CreateTableAsync<LocalStoreState>();
        await connection.CreateTableAsync<LocalInvoice>();
        await connection.CreateTableAsync<LocalInvoiceItem>();
        await connection.CreateTableAsync<OutboundSyncQueueItem>();
        await connection.CreateTableAsync<InboundChangeLogItem>();
        await connection.CreateTableAsync<LocalProductPrice>();
        await MigrateInboundChangeLogIfNeededAsync();
    }

    private async Task MigrateInboundChangeLogIfNeededAsync()
    {
        var columns = await connection.GetTableInfoAsync(nameof(InboundChangeLogItem));
        if (columns.Any(column => string.Equals(column.Name, "ChangeId", StringComparison.OrdinalIgnoreCase)))
        {
            await connection.ExecuteAsync($"DROP TABLE {nameof(InboundChangeLogItem)}");
            await connection.CreateTableAsync<InboundChangeLogItem>();
        }
    }

    public Task<List<LocalInvoice>> GetInvoicesAsync(Guid storeId) =>
        connection.Table<LocalInvoice>().Where(x => x.StoreId == storeId).OrderByDescending(x => x.UpdatedAtTicks).ToListAsync();

    public Task<List<LocalInvoiceItem>> GetInvoiceItemsAsync(Guid invoiceId) =>
        connection.Table<LocalInvoiceItem>().Where(x => x.InvoiceId == invoiceId).ToListAsync();

    public Task<List<LocalProductPrice>> GetProductsAsync(Guid storeId) =>
        connection.Table<LocalProductPrice>().Where(x => x.StoreId == storeId).ToListAsync();

    public async Task<List<InboundChangeLogItem>> GetConsumedInboundEventsAsync(Guid storeId)
    {
        var items = await connection.Table<InboundChangeLogItem>()
            .Where(x => x.StoreId == storeId)
            .OrderByDescending(x => x.ServerSequence)
            .ToListAsync();

        return items
            .GroupBy(x => x.EventId)
            .Select(group => group.OrderByDescending(x => x.ServerSequence).First())
            .OrderByDescending(x => x.ServerSequence)
            .ToList();
    }

    public Task<List<OutboundSyncQueueItem>> GetPendingQueueAsync(Guid storeId) =>
        connection.Table<OutboundSyncQueueItem>()
            .Where(x => x.StoreId == storeId && x.Status == (int)SyncState.Pending)
            .OrderBy(x => x.ClientLocalVersion)
            .ToListAsync();

    public async Task<int> GetPendingCountAsync(Guid storeId) => (await GetPendingQueueAsync(storeId)).Count;

    public async Task<LocalStoreState> GetOrCreateStoreStateAsync(Guid storeId, Guid deviceId)
    {
        var state = await connection.Table<LocalStoreState>().FirstOrDefaultAsync(x => x.StoreId == storeId && x.DeviceId == deviceId);
        if (state is not null)
        {
            return state;
        }

        state = new LocalStoreState
        {
            StoreId = storeId,
            DeviceId = deviceId,
            LastSyncCursor = 0,
            SnapshotComplete = false
        };

        await connection.InsertAsync(state);
        return state;
    }

    public Task UpdateStoreStateAsync(LocalStoreState state) => connection.UpdateAsync(state);

    public async Task<Guid> CreateInvoiceAsync(AppSession session, IReadOnlyList<LocalProductPrice> products)
    {
        var now = DateTimeOffset.UtcNow;
        var invoiceId = Guid.NewGuid();
        var localVersion = now.ToUnixTimeMilliseconds();
        var invoiceNumber = $"INV-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var chosenProducts = products.Take(2).ToList();

        var invoice = new LocalInvoice
        {
            InvoiceId = invoiceId,
            StoreId = session.StoreId,
            DeviceId = session.DeviceId,
            UserId = session.UserId,
            InvoiceNumber = invoiceNumber,
            Status = (int)InvoiceLifecycleStatus.Submitted,
            ServerVersion = 0,
            ServerSequence = null,
            SyncState = (int)SyncState.Pending,
            CreatedAtTicks = now.UtcTicks,
            UpdatedAtTicks = now.UtcTicks
        };

        await connection.InsertAsync(invoice);

        var items = chosenProducts.Select((product, index) => new LocalInvoiceItem
        {
            InvoiceItemId = Guid.NewGuid(),
            InvoiceId = invoiceId,
            ProductId = product.ProductId,
            ProductName = product.Name,
            Quantity = index + 1,
            UnitPrice = product.UnitPrice,
            LineTotal = product.UnitPrice * (index + 1)
        }).ToList();

        foreach (var item in items)
        {
            await connection.InsertAsync(item);
        }

        var payload = new
        {
            invoiceNumber,
            status = InvoiceLifecycleStatus.Submitted,
            items = items.Select(x => new InvoiceItemDto(x.InvoiceItemId, x.ProductId, x.ProductName, x.Quantity, x.UnitPrice, x.LineTotal)).ToList()
        };

        await connection.InsertAsync(new OutboundSyncQueueItem
        {
            QueueId = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            StoreId = session.StoreId,
            DeviceId = session.DeviceId,
            UserId = session.UserId,
            AggregateId = invoiceId,
            EventType = (int)InvoiceEventType.InvoiceCreated,
            IdempotencyKey = $"{session.StoreId:N}:{session.DeviceId:N}:{localVersion}",
            ClientLocalVersion = localVersion,
            ClientBaseServerVersion = 0,
            PayloadJson = JsonSerializer.Serialize(payload, MobileJson.Default),
            Status = (int)SyncState.Pending,
            CreatedAtTicks = now.UtcTicks
        });

        return invoiceId;
    }

    public async Task<Guid> SaveSalesInvoiceAsync(
        AppSession session,
        IReadOnlyList<LocalInvoiceItem> items,
        string? invoiceNumber = null,
        Guid? existingInvoiceId = null,
        long clientBaseServerVersion = 0)
    {
        if (items.Count == 0)
        {
            throw new InvalidOperationException("حداقل یک قلم کالا باید اضافه شود.");
        }

        var now = DateTimeOffset.UtcNow;
        var localVersion = now.ToUnixTimeMilliseconds();
        var isNew = existingInvoiceId is null;
        var invoiceId = existingInvoiceId ?? Guid.NewGuid();
        var number = string.IsNullOrWhiteSpace(invoiceNumber)
            ? $"SO-{DateTime.UtcNow:yyyyMMddHHmmss}"
            : invoiceNumber.Trim();

        var invoice = await connection.Table<LocalInvoice>().FirstOrDefaultAsync(x => x.InvoiceId == invoiceId);
        if (isNew)
        {
            invoice = new LocalInvoice
            {
                InvoiceId = invoiceId,
                StoreId = session.StoreId,
                DeviceId = session.DeviceId,
                UserId = session.UserId,
                InvoiceNumber = number,
                Status = (int)InvoiceLifecycleStatus.Submitted,
                ServerVersion = 0,
                SyncState = (int)SyncState.Pending,
                CreatedAtTicks = now.UtcTicks
            };
        }
        else if (invoice is null)
        {
            throw new InvalidOperationException("فاکتور یافت نشد.");
        }

        invoice!.InvoiceNumber = number;
        invoice.UpdatedAtTicks = now.UtcTicks;
        invoice.SyncState = (int)SyncState.Pending;

        if (isNew)
        {
            await connection.InsertAsync(invoice);
        }
        else
        {
            await connection.UpdateAsync(invoice);
            var oldItems = await GetInvoiceItemsAsync(invoiceId);
            foreach (var old in oldItems)
            {
                await connection.DeleteAsync(old);
            }
        }

        foreach (var item in items)
        {
            item.InvoiceId = invoiceId;
            if (item.InvoiceItemId == Guid.Empty)
            {
                item.InvoiceItemId = Guid.NewGuid();
            }

            item.LineTotal = item.Quantity * item.UnitPrice;
            await connection.InsertAsync(item);
        }

        var payload = new
        {
            invoiceNumber = number,
            status = InvoiceLifecycleStatus.Submitted,
            items = items.Select(x => new InvoiceItemDto(
                x.InvoiceItemId, x.ProductId, x.ProductName, x.Quantity, x.UnitPrice, x.LineTotal)).ToList()
        };

        await connection.InsertAsync(new OutboundSyncQueueItem
        {
            QueueId = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            StoreId = session.StoreId,
            DeviceId = session.DeviceId,
            UserId = session.UserId,
            AggregateId = invoiceId,
            EventType = (int)(isNew ? InvoiceEventType.InvoiceCreated : InvoiceEventType.InvoiceUpdated),
            IdempotencyKey = $"{session.StoreId:N}:{session.DeviceId:N}:{localVersion}",
            ClientLocalVersion = localVersion,
            ClientBaseServerVersion = isNew ? 0 : clientBaseServerVersion,
            PayloadJson = JsonSerializer.Serialize(payload, MobileJson.Default),
            Status = (int)SyncState.Pending,
            CreatedAtTicks = now.UtcTicks
        });

        return invoiceId;
    }

    public async Task ApplySnapshotAsync(StoreSnapshotResponse snapshot, Guid deviceId)
    {
        await connection.DeleteAllAsync<LocalInvoice>();
        await connection.DeleteAllAsync<LocalInvoiceItem>();
        await connection.DeleteAllAsync<LocalProductPrice>();

        foreach (var invoice in snapshot.Invoices)
        {
            await UpsertInvoiceAsync(invoice);
        }

        foreach (var product in snapshot.Products)
        {
            await connection.InsertAsync(new LocalProductPrice
            {
                ProductId = product.ProductId,
                StoreId = product.StoreId,
                Sku = product.Sku,
                Name = product.Name,
                UnitPrice = product.UnitPrice,
                Currency = product.Currency,
                UpdatedAtTicks = product.UpdatedAt.UtcTicks
            });
        }

        var state = await GetOrCreateStoreStateAsync(snapshot.StoreId, deviceId);
        state.LastSyncCursor = snapshot.SnapshotCursor;
        state.SnapshotComplete = true;
        await UpdateStoreStateAsync(state);
    }

    public async Task ApplyChangesAsync(Guid deviceId, IncrementalChangesResponse response)
    {
        foreach (var change in response.Changes)
        {
            var exists = await connection.Table<InboundChangeLogItem>().FirstOrDefaultAsync(x => x.EventId == change.EventId);
            if (exists is not null)
            {
                continue;
            }

            if (change.EventType is InvoiceEventType.InvoiceCreated or InvoiceEventType.InvoiceUpdated or InvoiceEventType.InvoiceVoided)
            {
                var invoice = change.Payload.Deserialize<InvoiceDto>(MobileJson.Default);
                if (invoice is not null)
                {
                    await UpsertInvoiceAsync(invoice);
                }
            }
            else if (change.EventType == InvoiceEventType.PriceUpdated)
            {
                var product = change.Payload.Deserialize<ProductPriceDto>(MobileJson.Default);
                if (product is not null)
                {
                    await connection.InsertOrReplaceAsync(new LocalProductPrice
                    {
                        ProductId = product.ProductId,
                        StoreId = product.StoreId,
                        Sku = product.Sku,
                        Name = product.Name,
                        UnitPrice = product.UnitPrice,
                        Currency = product.Currency,
                        UpdatedAtTicks = product.UpdatedAt.UtcTicks
                    });
                }
            }

            try
            {
                await connection.InsertAsync(new InboundChangeLogItem
                {
                    EventId = change.EventId,
                    StoreId = change.StoreId,
                    ServerSequence = change.ServerSequence,
                    EventType = (int)change.EventType,
                    PayloadJson = change.Payload.GetRawText(),
                    AppliedAtTicks = DateTimeOffset.UtcNow.UtcTicks
                });
            }
            catch (SQLiteException ex) when (ex.Result == SQLite3.Result.Constraint)
            {
                // Another sync pass may have recorded the same event.
            }
        }

        var state = await GetOrCreateStoreStateAsync(response.StoreId, deviceId);
        state.LastSyncCursor = response.ToCursor;
        await UpdateStoreStateAsync(state);
    }

    public async Task MarkUploadResultsAsync(Guid storeId, IReadOnlyList<SyncUploadResultDto> results)
    {
        foreach (var result in results)
        {
            var queue = await connection.Table<OutboundSyncQueueItem>().FirstOrDefaultAsync(x => x.EventId == result.EventId);
            if (queue is null)
            {
                continue;
            }

            queue.Status = (int)result.ResultingState;
            queue.LastError = result.ErrorMessage;
            await connection.UpdateAsync(queue);

            var invoice = await connection.Table<LocalInvoice>().FirstOrDefaultAsync(x => x.InvoiceId == queue.AggregateId && x.StoreId == storeId);
            if (invoice is null)
            {
                continue;
            }

            invoice.SyncState = (int)result.ResultingState;
            if (result.ServerVersion.HasValue)
            {
                invoice.ServerVersion = result.ServerVersion.Value;
            }

            if (result.ServerSequence.HasValue)
            {
                invoice.ServerSequence = result.ServerSequence.Value;
            }

            invoice.UpdatedAtTicks = DateTimeOffset.UtcNow.UtcTicks;
            await connection.UpdateAsync(invoice);
        }
    }

    private async Task UpsertInvoiceAsync(InvoiceDto invoice)
    {
        var existing = await connection.Table<LocalInvoice>().FirstOrDefaultAsync(x => x.InvoiceId == invoice.InvoiceId);
        if (existing is null)
        {
            existing = new LocalInvoice();
        }

        existing.InvoiceId = invoice.InvoiceId;
        existing.StoreId = invoice.StoreId;
        existing.DeviceId = invoice.DeviceId;
        existing.UserId = invoice.UserId;
        existing.InvoiceNumber = invoice.InvoiceNumber;
        existing.Status = (int)invoice.Status;
        existing.ServerVersion = invoice.ServerVersion;
        existing.ServerSequence = invoice.ServerSequence;
        existing.SyncState = (int)invoice.SyncState;
        existing.CreatedAtTicks = invoice.CreatedAt.UtcTicks;
        existing.UpdatedAtTicks = invoice.UpdatedAt.UtcTicks;

        await connection.InsertOrReplaceAsync(existing);

        var existingItems = await connection.Table<LocalInvoiceItem>().Where(x => x.InvoiceId == invoice.InvoiceId).ToListAsync();
        foreach (var item in existingItems)
        {
            await connection.DeleteAsync(item);
        }

        foreach (var item in invoice.Items)
        {
            await connection.InsertAsync(new LocalInvoiceItem
            {
                InvoiceItemId = item.InvoiceItemId,
                InvoiceId = invoice.InvoiceId,
                ProductId = item.ProductId,
                ProductName = item.ProductName,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                LineTotal = item.LineTotal
            });
        }
    }
}

public sealed class SyncApiClient(HttpClient httpClient)
{
    public async Task<LoginResponse> LoginAsync(string authUrl, string username, string password, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsync(
            $"{authUrl.TrimEnd('/')}{ApiRoutes.AuthLogin}",
            CreateJsonContent(new LoginRequest(username, password)),
            cancellationToken);

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<LoginResponse>(json, MobileJson.Default)
            ?? throw new InvalidOperationException("Login response was empty.");
    }

    public async Task<StoreSnapshotResponse> GetSnapshotAsync(string baseUrl, Guid storeId, string accessToken, CancellationToken cancellationToken = default)
    {
        ApplyAuthorizationHeader(accessToken);
        var json = await httpClient.GetStringAsync($"{baseUrl.TrimEnd('/')}{ApiRoutes.SyncSnapshot}?storeId={storeId}", cancellationToken);
        return JsonSerializer.Deserialize<StoreSnapshotResponse>(json, MobileJson.Default)
            ?? throw new InvalidOperationException("Snapshot response was empty.");
    }

    public async Task<IncrementalChangesResponse> GetChangesAsync(string baseUrl, Guid storeId, long sinceCursor, Guid deviceId, string accessToken, CancellationToken cancellationToken = default)
    {
        ApplyAuthorizationHeader(accessToken);
        var url = $"{baseUrl.TrimEnd('/')}{ApiRoutes.SyncChanges}?storeId={storeId}&sinceCursor={sinceCursor}&deviceId={deviceId}";
        var json = await httpClient.GetStringAsync(url, cancellationToken);
        return JsonSerializer.Deserialize<IncrementalChangesResponse>(json, MobileJson.Default)
            ?? throw new InvalidOperationException("Changes response was empty.");
    }

    public async Task<SyncUploadResponse> UploadAsync(string baseUrl, SyncUploadRequest request, string accessToken, CancellationToken cancellationToken = default)
    {
        ApplyAuthorizationHeader(accessToken);
        using var response = await httpClient.PostAsync(
            $"{baseUrl.TrimEnd('/')}{ApiRoutes.SyncEvents}",
            CreateJsonContent(request),
            cancellationToken);

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<SyncUploadResponse>(json, MobileJson.Default)
            ?? throw new InvalidOperationException("Upload response was empty.");
    }

    public async Task AckAsync(string baseUrl, SyncAckRequest request, string accessToken, CancellationToken cancellationToken = default)
    {
        ApplyAuthorizationHeader(accessToken);
        using var response = await httpClient.PostAsync(
            $"{baseUrl.TrimEnd('/')}{ApiRoutes.SyncAck}",
            CreateJsonContent(request),
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static StringContent CreateJsonContent<T>(T value)
    {
        var content = new StringContent(JsonSerializer.Serialize(value, MobileJson.Default), Encoding.UTF8);
        content.Headers.ContentType = new("application/json");
        return content;
    }

    private void ApplyAuthorizationHeader(string? accessToken)
    {
        httpClient.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(accessToken)
            ? null
            : new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
    }
}

public sealed class OfflineSyncService(MobileRepository repository, SyncApiClient apiClient, AppSession session)
{
    private readonly SemaphoreSlim syncGate = new(1, 1);

    public async Task InitializeAsync()
    {
        await repository.InitializeAsync();
        await repository.GetOrCreateStoreStateAsync(session.StoreId, session.DeviceId);
    }

    public async Task LoginAsync(string username, string password, string serverBaseUrl, CancellationToken cancellationToken = default)
    {
        var baseUrl = serverBaseUrl.TrimEnd('/');
        var response = await apiClient.LoginAsync(baseUrl, username, password, cancellationToken);
        session.AccessToken = response.AccessToken;
        session.UserId = response.UserId;
        session.Username = username;
        session.DisplayName = response.DisplayName;
        session.AuthUrl = baseUrl;
        session.BaseUrl = baseUrl;
        session.StoreId = response.Stores.FirstOrDefault()?.StoreId ?? DemoIdentity.DefaultStoreId;
    }

    public async Task LogoutAsync()
    {
        session.Clear();
        await Task.CompletedTask;
    }

    public async Task EnsureSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var state = await repository.GetOrCreateStoreStateAsync(session.StoreId, session.DeviceId);
        if (state.SnapshotComplete)
        {
            return;
        }

        var snapshot = await apiClient.GetSnapshotAsync(session.BaseUrl, session.StoreId, session.AccessToken, cancellationToken);
        await repository.ApplySnapshotAsync(snapshot, session.DeviceId);
    }

    public async Task<Guid> SaveSalesInvoiceAsync(
        IReadOnlyList<LocalInvoiceItem> items,
        string? invoiceNumber = null,
        Guid? existingInvoiceId = null,
        long clientBaseServerVersion = 0,
        CancellationToken cancellationToken = default)
    {
        var invoiceId = await repository.SaveSalesInvoiceAsync(session, items, invoiceNumber, existingInvoiceId, clientBaseServerVersion);
        _ = SyncSilentlyAsync(cancellationToken);
        return invoiceId;
    }

    public Task<List<LocalProductPrice>> GetProductsAsync() => repository.GetProductsAsync(session.StoreId);

    public Task<List<LocalInvoiceItem>> GetInvoiceItemsAsync(Guid invoiceId) =>
        repository.GetInvoiceItemsAsync(invoiceId);

    public async Task<LocalInvoice?> GetInvoiceAsync(Guid invoiceId)
    {
        var invoices = await repository.GetInvoicesAsync(session.StoreId);
        return invoices.FirstOrDefault(x => x.InvoiceId == invoiceId);
    }

    public async Task SyncSilentlyAsync(CancellationToken cancellationToken = default)
    {
        if (!session.IsLoggedIn || Connectivity.NetworkAccess != NetworkAccess.Internet)
        {
            return;
        }

        try
        {
            await SyncAsync(cancellationToken);
        }
        catch
        {
            // Offline or transient errors are expected during background sync.
        }
    }

    public async Task SyncAsync(CancellationToken cancellationToken = default)
    {
        if (!await syncGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            await EnsureSnapshotAsync(cancellationToken);
            var state = await repository.GetOrCreateStoreStateAsync(session.StoreId, session.DeviceId);
            var pending = await repository.GetPendingQueueAsync(session.StoreId);

            if (pending.Count > 0)
            {
                var upload = new SyncUploadRequest(
                    session.StoreId,
                    session.DeviceId,
                    pending.Select(x => new SyncEnvelopeDto(
                        x.EventId,
                        x.StoreId,
                        x.DeviceId,
                        x.UserId,
                        x.AggregateId,
                        (InvoiceEventType)x.EventType,
                        x.IdempotencyKey,
                        x.ClientLocalVersion,
                        x.ClientBaseServerVersion,
                        new DateTimeOffset(x.CreatedAtTicks, TimeSpan.Zero),
                        JsonSerializer.Deserialize<JsonElement>(x.PayloadJson))).ToList());

                var uploadResponse = await apiClient.UploadAsync(session.BaseUrl, upload, session.AccessToken, cancellationToken);
                await repository.MarkUploadResultsAsync(session.StoreId, uploadResponse.Results);
            }

            var changes = await apiClient.GetChangesAsync(session.BaseUrl, session.StoreId, state.LastSyncCursor, session.DeviceId, session.AccessToken, cancellationToken);
            await repository.ApplyChangesAsync(session.DeviceId, changes);

            if (changes.ToCursor > state.LastSyncCursor)
            {
                await apiClient.AckAsync(session.BaseUrl, new SyncAckRequest(session.StoreId, session.DeviceId, changes.ToCursor), session.AccessToken, cancellationToken);
            }
        }
        finally
        {
            syncGate.Release();
        }
    }

    public Task<List<LocalInvoice>> GetInvoicesAsync() => repository.GetInvoicesAsync(session.StoreId);
    public Task<int> GetPendingCountAsync() => repository.GetPendingCountAsync(session.StoreId);
    public Task<List<InboundChangeLogItem>> GetConsumedEventsAsync() => repository.GetConsumedInboundEventsAsync(session.StoreId);
}

public sealed class LocalStoreState
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    public Guid StoreId { get; set; }
    public Guid DeviceId { get; set; }
    public long LastSyncCursor { get; set; }
    public bool SnapshotComplete { get; set; }
}

public sealed class LocalInvoice
{
    [PrimaryKey]
    public Guid InvoiceId { get; set; }
    public Guid StoreId { get; set; }
    public Guid DeviceId { get; set; }
    public Guid UserId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public int Status { get; set; }
    public long ServerVersion { get; set; }
    public long? ServerSequence { get; set; }
    public int SyncState { get; set; }
    public long CreatedAtTicks { get; set; }
    public long UpdatedAtTicks { get; set; }

    [Ignore]
    public string PersianSyncState => (Neo.Contracts.SyncState)SyncState switch
    {
        Neo.Contracts.SyncState.Pending => "در انتظار همگام‌سازی",
        Neo.Contracts.SyncState.Synced => "همگام شده",
        Neo.Contracts.SyncState.Failed => "خطا در همگام‌سازی",
        Neo.Contracts.SyncState.Review => "نیاز به بررسی",
        _ => "نامشخص"
    };

    [Ignore]
    public string PersianStatus => (InvoiceLifecycleStatus)Status switch
    {
        InvoiceLifecycleStatus.Draft => "پیش‌نویس",
        InvoiceLifecycleStatus.Submitted => "ثبت شده",
        InvoiceLifecycleStatus.Voided => "باطل شده",
        _ => "نامشخص"
    };

    [Ignore]
    public string DisplayUpdatedAt => new DateTimeOffset(UpdatedAtTicks, TimeSpan.Zero).ToLocalTime().ToString("yyyy/MM/dd HH:mm");
}

public sealed class LocalInvoiceItem
{
    [PrimaryKey]
    public Guid InvoiceItemId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class OutboundSyncQueueItem
{
    [PrimaryKey]
    public Guid QueueId { get; set; }
    public Guid EventId { get; set; }
    public Guid StoreId { get; set; }
    public Guid DeviceId { get; set; }
    public Guid UserId { get; set; }
    public Guid AggregateId { get; set; }
    public int EventType { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public long ClientLocalVersion { get; set; }
    public long? ClientBaseServerVersion { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public int Status { get; set; }
    public string? LastError { get; set; }
    public long CreatedAtTicks { get; set; }
}

public sealed class InboundChangeLogItem
{
    [PrimaryKey]
    public Guid EventId { get; set; }
    public Guid StoreId { get; set; }
    public long ServerSequence { get; set; }
    public int EventType { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public long AppliedAtTicks { get; set; }

    [Ignore]
    public string PersianEventType => (InvoiceEventType)EventType switch
    {
        InvoiceEventType.InvoiceCreated => "ایجاد فاکتور",
        InvoiceEventType.InvoiceUpdated => "ویرایش فاکتور",
        InvoiceEventType.InvoiceVoided => "ابطال فاکتور",
        InvoiceEventType.PriceUpdated => "به‌روزرسانی قیمت",
        InvoiceEventType.StoreSyncChanged => "تغییر همگام‌سازی",
        _ => "نامشخص"
    };

    [Ignore]
    public string DisplayAppliedAt => new DateTimeOffset(AppliedAtTicks, TimeSpan.Zero).ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss");

    [Ignore]
    public string ShortEventId => $"{EventId:N}"[..13] + "…";
}

public sealed class LocalProductPrice
{
    [PrimaryKey]
    public Guid ProductId { get; set; }
    public Guid StoreId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public string Currency { get; set; } = "USD";
    public long UpdatedAtTicks { get; set; }
}

public static class MobileJson
{
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
