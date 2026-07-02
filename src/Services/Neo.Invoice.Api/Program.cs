using Neo.Contracts;

namespace Neo.Invoice.Api;

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

        app.MapGet("/health", () => Results.Ok(new { service = "invoice", status = "ok" }));

        app.MapGet(ApiRoutes.Invoices, () =>
        {
            return Results.Ok(Array.Empty<InvoiceDto>());
        });

        app.Run();
    }
}
