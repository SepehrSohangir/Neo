using Neo.Contracts;

namespace Neo.ProductPrice.Api;

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        });
        builder.Services.AddAuthorization();
        builder.Services.AddOpenApi();

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseAuthorization();

        app.MapGet("/health", () => Results.Ok(new { service = "product-price", status = "ok" }));

        app.MapGet(ApiRoutes.Products, (Guid? storeId) =>
        {
            var effectiveStoreId = storeId ?? DemoIdentity.DefaultStoreId;
            var products = new[]
            {
                new ProductPriceDto(DemoIdentity.ProductAppleId, effectiveStoreId, "APL-001", "Apple", 2.50m, "USD", DateTimeOffset.UtcNow),
                new ProductPriceDto(DemoIdentity.ProductMilkId, effectiveStoreId, "MLK-001", "Milk", 4.75m, "USD", DateTimeOffset.UtcNow)
            };

            return Results.Ok(products);
        });

        app.Run();
    }
}
