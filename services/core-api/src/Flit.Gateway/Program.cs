using System.Threading.RateLimiting;
using Flit.Gateway.Configuration;
using Flit.Gateway.Health;
using Flit.Gateway.Middleware;
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

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

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
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
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
app.UseCors();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
// Feature #11076 / HU #11104 / ADR-0039: obligatorio ANTES de MapReverseProxy.
// Sin UseWebSockets() YARP no negocia upgrade → SignalR cae a long-polling (AC3).
app.UseWebSockets();

app.MapHealthEndpoints();
app.MapReverseProxy();

app.Run();
