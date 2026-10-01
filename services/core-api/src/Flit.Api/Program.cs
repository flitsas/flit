using System.Text.Json;
using Flit.Admin.Application;
using Flit.Analytics.Application;
using Flit.Api.Authorization;
using Flit.Api.Hosting;
using Flit.Api.OpenApi;
using Flit.Api.Platform;
using Flit.Api.RateLimiting;
using Flit.Api.Identity;
using Flit.Infrastructure;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Security;
using Flit.Modules.Security.Application;
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

// HU #12895 (FLIT Suite A-02): la validación del contenedor de DI (scopes y construcción) queda fija y no depende del
// nombre del ambiente. En Development ya estaba activa, así que DEV, QA y PDN no cambian; un ambiente con otro nombre
// sigue detectando al arrancar los errores de DI en vez de descubrirlos en la primera petición.
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});

// Persistencia (EF Core + PostgreSQL) + servicios de seguridad/login (HU #10168).
var coreConnStr = builder.Configuration.GetConnectionString("Core")
    ?? builder.Configuration.GetConnectionString("FlitDb");

if (string.IsNullOrWhiteSpace(coreConnStr))
{
    throw new InvalidOperationException("ConnectionStrings:Core (PostgreSQL) es obligatoria.");
}

builder.Services.AddPostgresInfrastructure(coreConnStr, builder.Configuration, builder.Environment);
// Epic #13217 (HU #13232): login y recuperación de cuenta (Flit.Identity.Application). Transición: hasta el corte.
builder.Services.AddIdentityAuthApplication();

// Runtime de trámites (rework #10128): casos de uso de instancias/wizard/consultas.
builder.Services.AddTramitesApplication();

// Dashboard analítico (Feature #10139, HU #10243): handlers de lectura de agregados.
builder.Services.AddAnalyticsApplication();

// DR. FLIT (Épica #12718, ADR-0060): chat con LLM sobre el manual. Los puertos los registra Infrastructure.
Flit.DrFlit.Application.DrFlitApplicationServiceCollectionExtensions.AddDrFlitApplication(builder.Services);
Flit.Analytics.Application.Scheduling.AnalyticsSchedulingServiceCollectionExtensions.AddAnalyticsScheduling(builder.Services); // Reportes2 HU-D — CRUD de informes programados y alertas

// Seguridad: autenticación JWT + policy SuperAdmin (HU #10189, RF01).
builder.Services.AddApiSecurity(builder.Configuration, builder.Environment);

// Respuesta 401 con código SESSION_EXPIRED para tokens expirados (HU #10168, AC3) y cierre de sesión en toda la suite.
// Epic #13217 (HU #13232): compartido con core-identity (Flit.Suite.AspNetCore).
builder.Services.AddFlitSessionExpiredResponses();

// Módulo Admin (HU #10189, RF02).
builder.Services.AddAdminApplication();
// HU #12416 — AddAdminInfrastructure necesita builder.Configuration para ligar DomainOptions
// (sección Domains: reservados y CNAME del borde).
builder.Services.AddAdminInfrastructure(builder.Configuration);

// HU #12576 (Feature #12565) — orquestador API-layer de la decisión OT sobre una solicitud de
// revocatoria: compone Flit.Admin.Application (RevokeOtClientProcedureHandler, HU #12166) con
// Flit.Tramites.* (IProcedureRevocationRequestRepository/IRevocationRequestNotifier); ninguno de los
// dos módulos puede referenciar al otro, así que vive en Flit.Api (mismo criterio que la composición
// inline de AdminOtEndpoints.ApproveClientProcedureAsync).
builder.Services.AddScoped<Flit.Api.UseCases.RevocationRequests.DecideRevocationRequestHandler>();
builder.Services.AddScoped<Flit.Api.UseCases.RevocationRequests.GetActiveRevocationRequestHandler>();

// HU #12578 (Feature #12565) — listado dedicado "Revocatorias" del lado OT: mismo criterio de
// composición API-layer que la decisión de arriba (compone Admin + Tramites).
builder.Services.AddScoped<Flit.Api.UseCases.RevocationRequests.ListOtRevocationRequestsHandler>();

// HU #12711 — ¿el tenant del caller es un organismo de tránsito? (filtro de Validación de Identidad).
builder.Services.AddScoped<ITransitOfficeTenantProbe, TransitOfficeTenantProbe>();

// Handler de autorización por permisos del JWT (HU #10165).
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

// Handler para la policy AdminCompany (roles de empresa + SuperAdmin bypass).
builder.Services.AddSingleton<IAuthorizationHandler, AdminCompanyAuthorizationHandler>();

// Handler para la policy OtModule (SuperAdmin, ot_admin o tenant organismo de tránsito).
builder.Services.AddSingleton<IAuthorizationHandler, OtModuleAuthorizationHandler>();

// HU #12859 (Feature #12848, Épica #12751) — policy exclusiva de Prelación documental:
// SuperAdmin u ot_admin, sin el bypass de entity_type=TRANSIT_OFFICE de OtModulePolicy.
builder.Services.AddSingleton<IAuthorizationHandler, OtAdminOrSuperAdminAuthorizationHandler>();

// HU #12345 — cabeza de grupo (AdminCompany + is_group_parent en BD).
builder.Services.AddScoped<IAuthorizationHandler, GroupHeadCompanyAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, MarcaBlancaHeadCompanyAuthorizationHandler>();

// HU #12417 (Feature #12368, ADR-0060 D2) — DomainContext por petición (DomainContextMiddleware
// más abajo puebla HttpContext.Items; este accessor lo expone a Application sin acoplarla a HTTP).
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Flit.Modules.Security.Application.Auth.IDomainContextAccessor,
    Flit.Api.Authorization.HttpDomainContextAccessor>();

// HU #12418 (Feature #12366, ADR-0060 D2) — resolución pública/sesión de marca: sección
// PublicBranding (relleno de tiempo opcional + límite de tasa) y la PRIMERA policy de
// AddRateLimiter del repo (delta-hechos #1), solo para /public/branding y /public/branding/logos/*.
builder.Services.Configure<Flit.Api.RateLimiting.PublicBrandingOptions>(
    builder.Configuration.GetSection(Flit.Api.RateLimiting.PublicBrandingOptions.SectionName));
builder.Services.AddPublicBrandingRateLimiter();

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

// === FLIT Suite: servicios ===
// Una línea por frente que llama a su propio método de extensión (regla R5 de
// docs/suite/reglas-trabajo-paralelo.md). No se reordenan las líneas existentes.
builder.Services.AddPlatformApi(builder.Configuration); // Frente B · HU #12966
builder.Services.AddFlitOidc(builder.Configuration); // Frente A · HU #12990 (Suite:Oidc:Enabled)
// === FLIT Suite: fin servicios ===

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

    // Seed idempotente, DESPUÉS de migrar para que existan las tablas de identity/security. HU #12895 (A-02): el
    // catálogo RBAC (Seed:RbacCatalog) y las cuentas demo (Seed:DemoUsers) se encienden por separado; por defecto,
    // ambos solo en Development.
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    await DevelopmentAuthSeeder.SeedAsync(
        db, hasher, SeedSettings.From(app.Configuration, app.Environment), CancellationToken.None);
}

// Swagger UI: /swagger (doc en /swagger/v1/swagger.json). Va antes de auth para que la página de la UI sea accesible
// sin token; cada «Try it out» sí envía el JWT. HU #12895 (A-02): Swagger:Enabled lo decide, por defecto solo en
// Development; fuera de local se apaga con FLIT_SWAGGER_ENABLED=false en el .env.
if (app.Configuration.GetValue("Swagger:Enabled", app.Environment.IsDevelopment()))
{
    app.UseFlitSwagger();
}

app.UseCors(FrontendCorsPolicy);

// HU #12418 (Feature #12366, ADR-0060 D2) — límite de tasa de /public/branding* (delta-hechos #1:
// primera policy del repo). Tras CORS/routing, antes de auth/endpoints.
app.UseRateLimiter();

// HU #12417 (Feature #12368, ADR-0060 D2) — puebla DomainContext leyendo EXCLUSIVAMENTE el sello
// X-Flit-Domain que fija Flit.Gateway. Va ANTES de auth: el login/recuperación (#12422) necesita
// el dominio de la petición sin depender de un JWT (hoy sin validar — Bug diferido).
app.UseMiddleware<Flit.Api.Middleware.DomainContextMiddleware>();

app.UseAuthentication();

// HU #12422 (Feature #12369, ADR-0060 D3) — liga la sesión al dominio de emisión (claim "dom").
// Va DESPUÉS de auth (necesita HttpContext.User) y ANTES de authorization: una petición anónima
// (sin usuario autenticado) la atraviesa sin cambios.
app.UseMiddleware<Flit.Api.Authorization.DomainBindingMiddleware>();

app.UseAuthorization();

// Enforcement multi-tenant de los endpoints runtime de trámites (#1): resuelve el tenant desde el
// JWT (no del header del cliente) y deja superadmin con acceso multi-tenant. Va DESPUÉS de la auth
// (necesita HttpContext.User) y ANTES de los endpoints. No toca parametrización ni portal público.
app.UseMiddleware<Flit.Api.Middleware.TenantEnforcementMiddleware>();

// HU #12358 (Feature #12257) — guard único de escritura para cabezas de grupo: la lectura consolidada de
// la red no otorga escritura sobre los hijos. Va DESPUÉS del enforcement (usa el TenantScope de Items) y
// cubre /api/v1/tramites/instances/{id}/** y /api/v1/admin/tramites/{id}/** en todos los verbos de escritura.
app.UseMiddleware<Flit.Api.Middleware.TenantWriteGuardMiddleware>();

app.UseMiddleware<Flit.Api.Middleware.UsageTelemetryMiddleware>(); // Reportes2 HU-A
app.UseMiddleware<Flit.Api.Platform.RequireProductMiddleware>(); // FLIT Suite · HU #12966 — RequireProduct (Suite:ProductAccess:Enforce)

// Liveness: el healthcheck de Docker (docker-compose.prod.yml) y el /ready del
// Gateway sondean este endpoint. Debe existir en core-api, no solo en el Gateway.
app.MapGet("/health", () => Results.Ok(new { status = "alive" })).AllowAnonymous();
app.MapGet("/api/v1/health", () => Results.Ok(new { status = "alive" })).AllowAnonymous();
app.MapReadiness(); // HU #13224 — /health/ready: base alcanzable y sin migraciones pendientes

// Rutas del login (Flit.Identity.Web): core-api las atiende solo durante la transición, como respaldo del gateway de
// core-identity (Epic #13217; se quitan en el corte, HU #13235). Después, todo lo demás de core-api.
app.MapIdentityEndpoints();
app.MapApiEndpoints();

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
