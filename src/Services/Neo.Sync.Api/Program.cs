using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Neo.Contracts;
using Neo.Eventing;
using Neo.Sync.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        });
        builder.Services
            .AddAuthentication("Bearer")
            .AddJwtBearer("Bearer", options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = AuthDefaults.Issuer,
                    ValidateAudience = true,
                    ValidAudience = AuthDefaults.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthDefaults.SigningKey)),
                    ValidateLifetime = true
                };
            });
        builder.Services.AddAuthorization();
        builder.Services.AddOpenApi();
        builder.Services.AddSingleton<IIntegrationEventPublisher, LoggingKafkaEventPublisher>();
        builder.Services.AddScoped<SyncOrchestrator>();

        var connectionString = builder.Configuration.GetConnectionString("SqlServer");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            builder.Services.AddDbContext<SyncDbContext>(options => options.UseSqlServer(connectionString));
        }
        else
        {
            builder.Services.AddDbContext<SyncDbContext>(options => options.UseInMemoryDatabase("neo-sync"));
        }

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
            db.Database.EnsureCreated();
            var orchestrator = scope.ServiceProvider.GetRequiredService<SyncOrchestrator>();
            orchestrator.EnsureSeedDataAsync().GetAwaiter().GetResult();
        }

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/health", () => Results.Ok(new
        {
            service = "sync",
            status = "ok",
            storeId = DemoIdentity.DefaultStoreId
        }));

        app.MapGet(ApiRoutes.Stores, async (SyncDbContext db, CancellationToken cancellationToken) =>
        {
            var stores = await db.Stores
                .OrderBy(x => x.StoreName)
                .Select(x => new AuthStoreDto(x.StoreId, x.StoreCode, x.StoreName))
                .ToListAsync(cancellationToken);
            return Results.Ok(stores);
        }).RequireAuthorization();

        app.MapGet(ApiRoutes.Products, async (Guid storeId, SyncDbContext db, CancellationToken cancellationToken) =>
        {
            var products = await db.ProductPrices
                .Where(x => x.StoreId == storeId)
                .OrderBy(x => x.Name)
                .Select(x => new ProductPriceDto(x.ProductId, x.StoreId, x.Sku, x.Name, x.UnitPrice, x.Currency, x.UpdatedAt))
                .ToListAsync(cancellationToken);
            return Results.Ok(products);
        }).RequireAuthorization();

        app.MapGet(ApiRoutes.Invoices, async (Guid storeId, SyncDbContext db, CancellationToken cancellationToken) =>
        {
            var invoices = await db.Invoices
                .Include(x => x.Items)
                .Where(x => x.StoreId == storeId)
                .OrderByDescending(x => x.UpdatedAt)
                .ToListAsync(cancellationToken);
            return Results.Ok(invoices.Select(SyncOrchestrator.ToDto));
        }).RequireAuthorization();

        app.MapGet($"{ApiRoutes.Invoices}/{{invoiceId:guid}}", async (Guid invoiceId, SyncDbContext db, CancellationToken cancellationToken) =>
        {
            var invoice = await db.Invoices
                .Include(x => x.Items)
                .SingleOrDefaultAsync(x => x.InvoiceId == invoiceId, cancellationToken);

            return invoice is null ? Results.NotFound() : Results.Ok(SyncOrchestrator.ToDto(invoice));
        }).RequireAuthorization();

        app.MapGet(ApiRoutes.SyncSnapshot, async (Guid storeId, SyncOrchestrator orchestrator, CancellationToken cancellationToken) =>
        {
            var snapshot = await orchestrator.GetSnapshotAsync(storeId, cancellationToken);
            return Results.Ok(snapshot);
        }).RequireAuthorization();

        app.MapGet(ApiRoutes.SyncChanges, async (
            Guid storeId,
            long sinceCursor,
            int? limit,
            Guid? deviceId,
            SyncOrchestrator orchestrator,
            CancellationToken cancellationToken) =>
        {
            var changes = await orchestrator.GetChangesAsync(storeId, sinceCursor, Math.Clamp(limit ?? 100, 1, 500), deviceId, cancellationToken);
            return Results.Ok(changes);
        }).RequireAuthorization();

        app.MapPost(ApiRoutes.SyncEvents, async (SyncUploadRequest request, SyncOrchestrator orchestrator, CancellationToken cancellationToken) =>
        {
            var response = await orchestrator.ProcessUploadAsync(request, cancellationToken);
            return Results.Ok(response);
        }).RequireAuthorization();

        app.MapPost(ApiRoutes.SyncAck, async (SyncAckRequest request, SyncOrchestrator orchestrator, CancellationToken cancellationToken) =>
        {
            await orchestrator.AcknowledgeAsync(request, cancellationToken);
            return Results.Accepted();
        }).RequireAuthorization();

        app.Run();
    }
}
