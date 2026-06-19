using Flit.Admin.Application;
using Flit.Api.Authorization;
using Flit.Api.Endpoints;
using Flit.Infrastructure;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Persistencia (EF Core + PostgreSQL).
var coreConnStr = builder.Configuration.GetConnectionString("Core")
    ?? builder.Configuration.GetConnectionString("FlitDb");

if (!string.IsNullOrWhiteSpace(coreConnStr))
{
    builder.Services.AddPostgresInfrastructure(
        coreConnStr, builder.Configuration, builder.Environment);
}
else
{
    throw new InvalidOperationException(
        "ConnectionStrings:Core (PostgreSQL) es obligatoria.");
}

// Seguridad: JWT + policy SuperAdmin (HU #10189, RF01).
builder.Services.AddApiSecurity(builder.Configuration, builder.Environment);

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
}

app.UseAuthentication();
app.UseAuthorization();

// Liveness: el healthcheck de Docker (docker-compose.prod.yml) y el /ready del
// Gateway sondean este endpoint. Debe existir en core-api, no solo en el Gateway.
app.MapGet("/health", () => Results.Ok(new { status = "alive" })).AllowAnonymous();

app.MapAdminCompaniesEndpoints();
app.MapAdminTransitOfficesEndpoints();
app.MapAdminDocumentTypesEndpoints();
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
