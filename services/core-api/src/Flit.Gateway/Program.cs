using System.Threading.RateLimiting;
using Flit.Gateway.Configuration;
using Flit.Gateway.Cors;
using Flit.Gateway.Health;
using Flit.Gateway.Middleware;
using Flit.Gateway.Transforms;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(new Serilog.Formatting.Json.JsonFormatter()));

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection(RateLimitOptions.SectionName));

// HU #12417 (Feature #12368, ADR-0060 D2) — sello de dominio + CORS derivados de los dominios
// registrados: la lista fija de Cors:AllowedOrigins NO cambia (AC4), se UNE en tiempo real a
// https://{host} de cada dominio activo. SetIsOriginAllowed (más abajo, tras Build) evalúa por
// petición — dar de alta un dominio se refleja sin redespliegue (AC3).
var fixedCorsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors();
builder.Services.AddMemoryCache();
builder.Services.Configure<InternalApiOptions>(builder.Configuration.GetSection(InternalApiOptions.SectionName));
var internalApiOptionsAtStartup = builder.Configuration.GetSection(InternalApiOptions.SectionName).Get<InternalApiOptions>()
    ?? new InternalApiOptions();
builder.Services.AddHttpClient(DynamicCorsOriginSource.HttpClientName, client =>
{
    client.BaseAddress = new Uri(internalApiOptionsAtStartup.ApiBaseUrl, UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddSingleton<DynamicCorsOriginSource>();

// HU #12417 AC1/AC5 — sello X-Flit-Domain: se aplica a TODA ruta YARP (AddTransforms, no por
// ruta individual). La excepción de red interna (delta-hechos-post-adr.md hecho 7) es opt-in vía
// DomainSeal:InternalAllowedNetworks (vacío por defecto — fail-closed).
builder.Services.Configure<DomainSealOptions>(builder.Configuration.GetSection(DomainSealOptions.SectionName));

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
var hasJwtSigningKey = jwt.TryGetSigningKey(builder.Environment, out var jwtSigningKey);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        if (hasJwtSigningKey && jwtSigningKey is not null)
        {
            opt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwt.Issuer,
                ValidateAudience = true,
                ValidAudience = jwt.Audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = jwtSigningKey,
                ClockSkew = TimeSpan.FromSeconds(30)
            };
            return;
        }

        // Login deshabilitado temporalmente (todos los ambientes): si no hay llave
        // de firma, se acepta el token sin validar firma en lugar de abortar el
        // arranque. Cuando exista jwt-public.pem, se valida la firma normalmente.
        Log.Warning(
            "JWT public key no encontrada — validación de firma deshabilitada. " +
            "Login no requerido (configuración temporal).");
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false,
            ValidateIssuerSigningKey = false,
            SignatureValidator = (token, _) => new JsonWebToken(token)
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("JwtRequired", p =>
        // Login deshabilitado temporalmente en TODOS los ambientes: el gateway no
        // exige usuario autenticado para enrutar /api, /hubs. Para reactivar el
        // login, restaurar el branch por entorno con p.RequireAuthenticatedUser().
        p.RequireAssertion(_ => true));

var rate = builder.Configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>() ?? new RateLimitOptions();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetTokenBucketLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
            factory: _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = rate.PerIpPermitsPerMinute,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                TokensPerPeriod = rate.PerIpPermitsPerMinute,
                AutoReplenishment = true,
                QueueLimit = 0
            }));
    o.AddPolicy("login-endpoint", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rate.LoginEndpointPermitsPerMinute,
                Window = TimeSpan.FromMinutes(1)
            }));
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("flit-gateway"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter());

var reverseProxyBuilder = builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms<DomainSealTransform>();
if (builder.Environment.IsDevelopment())
{
    Log.Warning(
        "Development: JWT no exigido en rutas YARP (sin login en frontend). " +
        "Activar JwtRequired al integrar autenticación.");
    reverseProxyBuilder.AddConfigFilter<DevelopmentNoJwtProxyConfigFilter>();
}

builder.Services.AddHealthChecks();
builder.Services.AddHttpClient();

var app = builder.Build();

app.UseSerilogRequestLogging();

// HU #12417 AC3/AC4 — orígenes dinámicos: SetIsOriginAllowed (NO WithOrigins estático) se evalúa
// EN CADA petición contra DynamicCorsOriginSource (lista fija ∪ dominios activos, caché 60 s,
// respaldo a la última lista buena). GetAwaiter().GetResult() es seguro aquí: el caché en memoria
// resuelve de forma síncrona salvo el primer refresco tras expirar el TTL o el arranque en frío.
var dynamicCorsOriginSource = app.Services.GetRequiredService<DynamicCorsOriginSource>();
app.UseCors(policy => policy
    .SetIsOriginAllowed(origin => dynamicCorsOriginSource
        .IsOriginAllowedAsync(origin, fixedCorsOrigins)
        .GetAwaiter().GetResult())
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials());

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// HU #12417 AC3 — YARP NO publica /api/v1/internal/*: el Gateway lo consume DIRECTO
// (DynamicCorsOriginSource, fuera del proxy). Cualquier intento de atravesarlo por la ruta pública
// recibe 404 antes de llegar al reverse proxy, sin tocar ict-host-route ni el resto de rutas.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/v1/internal", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    await next(context);
});

app.MapHealthEndpoints();
app.MapReverseProxy();

app.Run();
