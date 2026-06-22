using System.Text.Json;
using Flit.Admin.Application;
using Flit.Api.Authorization;
using Flit.Api.Endpoints;
using Flit.Infrastructure;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Security;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
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

// Migraciones automáticas al arrancar: valida si hay migraciones pendientes
// (comparando contra __EFMigrationsHistory) y aplica solo las que faltan. Si no
// hay pendientes es un no-op. La estrategia de reintentos de Npgsql
// (EnableRetryOnFailure) cubre cortes transitorios de conexión durante el arranque.
// Se puede desactivar con Database__AutoMigrate=false (p. ej. si se delega al CD).
if (app.Configuration.GetValue("Database:AutoMigrate", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    var pending = db.Database.GetPendingMigrations().ToList();
    if (pending.Count > 0)
    {
        var migrationNames = string.Join(", ", pending);
        MigrationLog.ApplyingMigrations(logger, pending.Count, migrationNames);
        db.Database.Migrate();
        MigrationLog.MigrationsApplied(logger);
    }
    else
    {
        MigrationLog.NoPendingMigrations(logger);
    }

    // Seed de datos de desarrollo (usuario demo para login: demo@flit.local / DemoPass1!).
    // Idempotente (no recrea si ya existe) y no-op fuera de Development. Corre DESPUÉS de
    // migrar para que existan las tablas de identity/security. Antes faltaba esta llamada,
    // por eso identity.users quedaba vacía y no se podía iniciar sesión.
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    await DevelopmentAuthSeeder.SeedAsync(db, hasher, app.Environment, CancellationToken.None);
}

app.UseAuthentication();
app.UseAuthorization();

// Liveness: el healthcheck de Docker (docker-compose.prod.yml) y el /ready del
// Gateway sondean este endpoint. Debe existir en core-api, no solo en el Gateway.
app.MapGet("/health", () => Results.Ok(new { status = "alive" })).AllowAnonymous();

app.MapAuthEndpoints();
app.MapAdminCompaniesEndpoints();
app.MapAdminTransitOfficesEndpoints();
app.MapAdminDocumentTypesEndpoints();
app.MapAdminProcedureDocumentRequirementsEndpoints();
app.MapAdminDocumentOrderOverridesEndpoints();
app.MapAdminDocumentRequirementOverridesEndpoints();
app.MapAdminResolvedDocumentMatrixEndpoints();
app.MapTramitesEndpoints();
app.MapTransfersEndpoints();

app.Run();

/// <summary>Punto de entrada expuesto para pruebas de integración (WebApplicationFactory).</summary>
public partial class Program;

/// <summary>
/// Logging de alto rendimiento (source-generated) para la migración automática al
/// arranque. Usa delegados <c>LoggerMessage</c> para cumplir CA1848.
/// </summary>
internal static partial class MigrationLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Aplicando {Count} migración(es) pendiente(s): {Migrations}")]
    public static partial void ApplyingMigrations(ILogger logger, int count, string migrations);

    [LoggerMessage(Level = LogLevel.Information, Message = "Migraciones aplicadas correctamente.")]
    public static partial void MigrationsApplied(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Base de datos al día: no hay migraciones pendientes.")]
    public static partial void NoPendingMigrations(ILogger logger);
}
