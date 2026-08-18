using System.Text.Json;
using Flit.Admin.Application;
using Flit.Analytics.Application;
using Flit.Api.Authorization;
using Flit.Api.Endpoints.Analytics;
using Flit.Api.Endpoints;
using Flit.Api.Endpoints.Public;
using Flit.Api.Endpoints.SuperAdmin;
using Flit.Api.Endpoints.Tramites;
using Flit.Api.OpenApi;
using Flit.Infrastructure;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Security;
using Flit.Modules.Security.Domain.Auth;
using Flit.Tramites.Application;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

// Plano C: core-api hace push gRPC hacia core-ict (callback de estado) sobre HTTP/2 en claro (h2c) en la
// red interna. Sin este switch, el cliente gRPC exige TLS y la llamada saliente falla.
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

// Persistencia (EF Core + PostgreSQL) + servicios de seguridad/login (HU #10168).
var coreConnStr = builder.Configuration.GetConnectionString("Core")
    ?? builder.Configuration.GetConnectionString("FlitDb");

if (string.IsNullOrWhiteSpace(coreConnStr))
{
    throw new InvalidOperationException("ConnectionStrings:Core (PostgreSQL) es obligatoria.");
}

builder.Services.AddPostgresInfrastructure(coreConnStr, builder.Configuration, builder.Environment);

// Runtime de trámites (rework #10128): casos de uso de instancias/wizard/consultas.
builder.Services.AddTramitesApplication();

// Dashboard analítico (Feature #10139, HU #10243): handlers de lectura de agregados.
builder.Services.AddAnalyticsApplication();
Flit.Analytics.Application.Scheduling.AnalyticsSchedulingServiceCollectionExtensions.AddAnalyticsScheduling(builder.Services); // Reportes2 HU-D — CRUD de informes programados y alertas

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

// Handler de autorización por permisos del JWT (HU #10165).
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

// Handler para la policy AdminCompany (roles de empresa + SuperAdmin bypass).
builder.Services.AddSingleton<IAuthorizationHandler, AdminCompanyAuthorizationHandler>();

// Swagger/OpenAPI: documento generado desde los endpoints. La UI se monta solo en
// Development (más abajo), pero el generador se registra siempre para no divergir.
builder.Services.AddFlitSwagger();

// CORS: el frontend (Next.js) corre en otro origen (localhost:3000 en dev) y el
// navegador bloquea las llamadas cross-origin sin esta política. Los orígenes son
// configurables vía Cors:AllowedOrigins; en dev se asume el frontend local.
const string FrontendCorsPolicy = "flit-frontend-cors";
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:3000", "http://localhost:4001"];
builder.Services.AddCors(options => options.AddPolicy(
    FrontendCorsPolicy,
    policy => policy
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

// gRPC server para la orquestación desde core-ict (ICT): crear el borrador reutilizando los casos
// de uso de trámites.
builder.Services.AddGrpc();

// El gRPC (h2c) necesita un endpoint dedicado SOLO HTTP/2: Kestrel no multiplexa HTTP/1.1 y HTTP/2
// en el mismo puerto en texto plano (el REST lo rechazaría con HTTP_1_1_REQUIRED). Es opt-in por
// Ict:GrpcPort — si no está configurado, el binding de la API REST queda EXACTAMENTE como estaba.
// Al declarar endpoints por código Kestrel ignora ASPNETCORE_URLS/launchSettings, así que se
// re-declara el endpoint REST desde esas mismas URLs (mismo puerto/host que hoy) y se añade el gRPC.
var ictGrpcPort = builder.Configuration.GetValue<int?>("Ict:GrpcPort");
if (ictGrpcPort is { } grpcPort)
{
    builder.WebHost.ConfigureKestrel((context, options) =>
    {
        var restUrls = (context.Configuration[WebHostDefaults.ServerUrlsKey] ?? "http://localhost:4003")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var raw in restUrls)
        {
            var uri = new Uri(raw
                .Replace("://+", "://0.0.0.0", StringComparison.Ordinal)
                .Replace("://*", "://0.0.0.0", StringComparison.Ordinal));
            if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                options.ListenLocalhost(uri.Port, lo => lo.Protocols = HttpProtocols.Http1AndHttp2);
            }
            else
            {
                options.ListenAnyIP(uri.Port, lo => lo.Protocols = HttpProtocols.Http1AndHttp2);
            }
        }

        options.ListenAnyIP(grpcPort, lo => lo.Protocols = HttpProtocols.Http2);
    });
}

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

// Swagger UI solo en Development: /swagger (doc en /swagger/v1/swagger.json). No se
// expone en producción (la API es de borde tras el Gateway). Va antes de auth para que
// la página de la UI sea accesible sin token; cada «Try it out» sí envía el JWT.
if (app.Environment.IsDevelopment())
{
    app.UseFlitSwagger();
}

app.UseCors(FrontendCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

// Enforcement multi-tenant de los endpoints runtime de trámites (#1): resuelve el tenant desde el
// JWT (no del header del cliente) y deja superadmin con acceso multi-tenant. Va DESPUÉS de la auth
// (necesita HttpContext.User) y ANTES de los endpoints. No toca parametrización ni portal público.
app.UseMiddleware<Flit.Api.Middleware.TenantEnforcementMiddleware>();

app.UseMiddleware<Flit.Api.Middleware.UsageTelemetryMiddleware>(); // Reportes2 HU-A

// Liveness: el healthcheck de Docker (docker-compose.prod.yml) y el /ready del
// Gateway sondean este endpoint. Debe existir en core-api, no solo en el Gateway.
app.MapGet("/health", () => Results.Ok(new { status = "alive" })).AllowAnonymous();
app.MapGet("/api/v1/health", () => Results.Ok(new { status = "alive" })).AllowAnonymous();

// Orquestación ICT (core-ict -> core-api): exige el service-token (esquema/policy IctService) para que
// solo core-ict autenticado como sistema pueda invocar la orquestación (no un tercero en el puerto interno).
app.MapGrpcService<Flit.Api.Grpc.IctOrchestrationService>()
    .RequireAuthorization(Flit.Api.Authorization.ApiSecurityExtensions.IctServicePolicy);
// Consulta de fuentes externas para ICT (reusa el subsistema de consultas de core-api).
app.MapGrpcService<Flit.Api.Grpc.IctConsultationService>()
    .RequireAuthorization(Flit.Api.Authorization.ApiSecurityExtensions.IctServicePolicy);

// ── Endpoints de seguridad + Admin/parametrización (develop) ──────────────────
app.MapAuthEndpoints();
app.MapSecurityEndpoints();
app.MapUserUiPreferencesEndpoints();
app.MapAdminCompaniesEndpoints();
app.MapAdminOtEndpoints();
app.MapAdminOtMetricsEndpoints();
app.MapAdminOtQueriesEndpoints();
app.MapAdminPlateRangesEndpoints();
app.MapOtIntegrationEndpoints();
app.MapAdminTransitOfficesEndpoints();
app.MapAdminQuipuxEndpoints();
app.MapAdminPlataformaMandatosEndpoints();
app.MapAdminPlataformaNotificacionesEndpoints();
app.MapAdminPlataformaNotificacionesPlantillasEndpoints();
app.MapAdminLogQxEndpoints();
app.MapAdminTransitOfficeTenantsEndpoints();
app.MapAdminMandateSignersEndpoints();
app.MapAdminCompanyMandateSignersEndpoints();
app.MapAdminMandateSignerIdentityEndpoints();
app.MapAdminSignatureVaultEndpoints();
app.MapAdminLegalRepresentativesEndpoints();
app.MapAdminDeedsEndpoints();
app.MapAdminPersonalizedDocumentsEndpoints();
app.MapAdminCompanyNotificationDeliveryLogsEndpoints();
app.MapAdminLegalRepresentativeIdentityEndpoints();
app.MapAdminDocumentTypesEndpoints();
app.MapAdminRejectionReasonsEndpoints();
app.MapAdminProcedureDocumentRequirementsEndpoints();
app.MapAdminDocumentOrderOverridesEndpoints();
app.MapAdminDocumentRequirementOverridesEndpoints();
app.MapAdminOtPrendaDocumentPolicyEndpoints();
app.MapAdminResolvedDocumentMatrixEndpoints();
app.MapAdminCompanyDocumentParamsEndpoints();
app.MapAdminImprontasEndpoints();
app.MapTramitesEndpoints();
app.MapTransfersEndpoints();

// ── Runtime de trámites (rework #10128) ───────────────────────────────────────
app.MapSuperAdminEndpoints();
app.MapPublicProcedureEndpoints();
app.MapPublicProcedureTypeEndpoints();
app.MapPublicBiometricaEndpoints();
app.MapPublicKyverumWebhookEndpoints();
app.MapPublicPortalEndpoints();
app.MapTramitesInstanceEndpoints();
app.MapTramitesActorEndpoints();
// HU #11196 / #11197 — firma a posteriori: marcar el trámite y consultar si la opción aplica.
app.MapTramitesFirmaPosteriorEndpoints();
app.MapTramitesAttachmentEndpoints();
app.MapTramitesOcrEndpoints();
app.MapTramitesParticipantEndpoints();
app.MapTramitesBiometricaEndpoints();
app.MapTramitesFirmaEndpoints();
app.MapTramitesFurEndpoints();
app.MapTramitesConsolidadoEndpoints();
app.MapConsultationEndpoints();
app.MapTramitesCommercialEndpoints();
app.MapTramitesPreflightEndpoints();
app.MapTramitesRnmcEndpoints();
app.MapTramitesWizardEndpoints();
app.MapTramitesVehicleColorsEndpoints();
app.MapTramitesVehicleServiceTypesEndpoints();
app.MapTramitesStatusHistoryEndpoints();
app.MapTramitesNotificationDispatchesEndpoints();
app.MapLegalRepresentativeConsumptionEndpoints();

// ── Dashboard analítico (Feature #10139) ──────────────────────────────────────
app.MapAnalyticsEndpoints();
app.MapDetailedReportEndpoints(); // Feature #10813
app.MapReportSchedulesEndpoints(); // Reportes2 HU-D
app.MapSuperAdminReportSchedulesEndpoints(); // Reportes2 HU-D 2da ola — informes de consulta SuperAdmin
app.MapAlertRulesEndpoints(); // Reportes2 HU-D
app.MapAnalyticsMetricsEndpoints(); // Reportes2 HU-B
app.MapCompanyQueriesEndpoints(); // Consultas propias de la empresa
app.MapSuperAdminQueriesEndpoints(); // Consultas de SuperAdmin sobre todas las compañías
app.MapUsageEventsEndpoints(); // Reportes2 HU-A

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
