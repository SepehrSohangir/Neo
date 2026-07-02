using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Neo.Contracts;

namespace Neo.Auth.Api;

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

        app.MapGet("/health", () => Results.Ok(new { service = "auth", status = "ok" }));

        app.MapPost(ApiRoutes.AuthLogin, (LoginRequest request, HttpContext httpContext) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest("Username and password are required.");
            }

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthDefaults.SigningKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, DemoIdentity.DemoUserId.ToString()),
                new("store_id", DemoIdentity.DefaultStoreId.ToString()),
                new(ClaimTypes.Name, request.Username)
            };

            var token = new JwtSecurityToken(
                issuer: AuthDefaults.Issuer,
                audience: AuthDefaults.Audience,
                claims: claims,
                expires: DateTime.UtcNow.AddHours(8),
                signingCredentials: credentials);

            var response = new LoginResponse(
                new JwtSecurityTokenHandler().WriteToken(token),
                DemoIdentity.DemoUserId,
                request.Username,
                [new AuthStoreDto(DemoIdentity.DefaultStoreId, "STORE-001", "Downtown Market")],
                $"{httpContext.Request.Scheme}://{httpContext.Request.Host}");

            return Results.Ok(response);
        });

        app.Run();
    }
}
