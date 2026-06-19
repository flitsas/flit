using System.Text.Json;
using Flit.Admin.Application;
using Flit.Api.Authorization;
using Flit.Api.Endpoints;
using Flit.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Persistencia (EF Core + PostgreSQL) + servicios de seguridad/login (HU #10168).
var coreConnStr = builder.Configuration.GetConnectionString("Core")
    ?? builder.Configuration.GetConnectionString("FlitDb");

if (string.IsNullOrWhiteSpace(coreConnStr))
{
    throw new InvalidOperationException("ConnectionStrings:Core (PostgreSQL) es obligatoria.");
}

builder.Services.AddPostgresInfrastructure(coreConnStr, builder.Configuration, builder.Environment);

// Seguridad: autenticación JWT + policy SuperAdmin (HU #10189, RF01).
builder.Services.AddApiSecurity(builder.Configuration, builder.Environment);

// Respuesta 401 con código SESSION_EXPIRED para tokens expirados (HU #10168, AC3).
// Aditivo sobre AddApiSecurity: solo fija Events, sin alterar TokenValidationParameters.
builder.Services.PostConfigure<JwtBearerOptions>(
    JwtBearerDefaults.AuthenticationScheme,
    options => options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            if (context.Exception is SecurityTokenExpiredException)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    code = "SESSION_EXPIRED",
                    message = "Session expired. Please sign in again.",
                }));
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
                return context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    code = "SESSION_EXPIRED",
                    message = "Session expired. Please sign in again.",
                }));
            }

            return Task.CompletedTask;
        },
    });

// Módulo Admin (HU #10189, RF02).
builder.Services.AddAdminApplication();
builder.Services.AddAdminInfrastructure();

var app = builder.Build();

// Migración + seed de datos de desarrollo: solo en dev local y bajo flag explícito.
// Evita que los tests de integración (WebApplicationFactory) intenten conectar a PostgreSQL.
if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Database:RunStartupMigrations"))
{
    await app.Services.InitializeInfrastructureAsync();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapAdminCompaniesEndpoints();
app.MapAdminTransitOfficesEndpoints();
app.MapTransfersEndpoints();

app.Run();

/// <summary>Punto de entrada expuesto para pruebas de integración (WebApplicationFactory).</summary>
public partial class Program;
