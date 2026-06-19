using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Flit.Api.Endpoints;
using Flit.Infrastructure;
using Flit.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var coreConnStr = builder.Configuration.GetConnectionString("Core")
    ?? builder.Configuration.GetConnectionString("FlitDb");

if (string.IsNullOrWhiteSpace(coreConnStr))
{
    throw new InvalidOperationException("ConnectionStrings:Core (PostgreSQL) es obligatoria.");
}

builder.Services.AddPostgresInfrastructure(coreConnStr, builder.Configuration, builder.Environment);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                if (context.Exception is SecurityTokenExpiredException)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.ContentType = "application/json";
                    var payload = JsonSerializer.Serialize(new
                    {
                        code = "SESSION_EXPIRED",
                        message = "Session expired. Please sign in again.",
                    });
                    return context.Response.WriteAsync(payload);
                }

                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                if (context.AuthenticateFailure is SecurityTokenExpiredException)
                {
                    context.HandleResponse();
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.ContentType = "application/json";
                    var payload = JsonSerializer.Serialize(new
                    {
                        code = "SESSION_EXPIRED",
                        message = "Session expired. Please sign in again.",
                    });
                    return context.Response.WriteAsync(payload);
                }

                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<JwtKeyMaterial>((options, keyMaterial) =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = keyMaterial.Issuer,
            ValidateAudience = true,
            ValidAudience = keyMaterial.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = keyMaterial.SigningKey,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtRegisteredClaimNames.Sub,
            RoleClaimType = "role_code",
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

await app.Services.InitializeInfrastructureAsync();

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();

app.Run();
