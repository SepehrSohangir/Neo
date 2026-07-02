using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Neo.Contracts;

namespace Neo.Gateway;

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
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
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("authenticated", policy => policy.RequireAuthenticatedUser());
        });
        builder.Services.AddOpenApi();
        builder.Services.AddReverseProxy()
            .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/health", () => Results.Ok(new { service = "gateway", status = "ok" }));
        app.MapReverseProxy();

        app.Run();
    }
}
