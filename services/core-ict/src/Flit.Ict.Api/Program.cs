using Flit.Ict.Api.Authorization;
using Flit.Ict.Api.Endpoints;
using Flit.Ict.Api.Grpc;
using Flit.Ict.Application;
using Flit.Ict.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Core;

// Permite gRPC sobre HTTP/2 en claro (h2c) hacia core-api en la red interna (sin TLS).
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

builder.Services.AddIctInfrastructure(builder.Configuration);
builder.Services.AddIctApplication();
builder.Services.AddIctApiSecurity(builder.Configuration);
// Token de plataforma (Logs/Trazabilidad/Clientes): firma, emisor, audiencia y vencimiento; fail-fast sin llave.
builder.Services.AddIctPlatformTokenValidation(builder.Configuration, builder.Environment);
builder.Services.AddGrpc();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Callback gRPC (core-api → core-ict) necesita un endpoint SOLO HTTP/2: Kestrel no multiplexa HTTP/1.1 y
// HTTP/2 en el mismo puerto en texto plano (HTTP_1_1_REQUIRED). Opt-in por Ict:GrpcPort — si no está,
// el binding REST queda como estaba (ASPNETCORE_URLS). Misma razón que Ict:GrpcPort en core-api.
// Si gitops/compose ya define Kestrel:Endpoints:Grpc (puerto h2c explícito), no re-declarar Listen
// para no chocar con ese binding.
var ictGrpcPort = builder.Configuration.GetValue<int?>("Ict:GrpcPort");
var hasExplicitGrpcEndpoint = !string.IsNullOrWhiteSpace(
    builder.Configuration["Kestrel:Endpoints:Grpc:Url"]);
if (ictGrpcPort is { } grpcPort && !hasExplicitGrpcEndpoint)
{
    builder.WebHost.ConfigureKestrel((context, options) =>
    {
        var restUrls = (context.Configuration[WebHostDefaults.ServerUrlsKey] ?? "http://localhost:4020")
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

// HU #12895 (FLIT Suite A-02): Swagger:Enabled lo decide, por defecto solo en Development (igual que core-api).
if (app.Configuration.GetValue("Swagger:Enabled", app.Environment.IsDevelopment()))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseIctPlatformToken();
app.UseAuthorization();

// Logging de requests entrantes de cliente ICT (rutas v1, redactado, en scope propio). Tras la auth para
// resolver el tenant del token ICT.
app.UseMiddleware<Flit.Ict.Api.Middleware.IctRequestLoggingMiddleware>();

app.MapIctAuthEndpoints();
app.MapIctRegisterEndpoints();
app.MapIctPreTramiteEndpoints();
app.MapIctAttachmentEndpoints();
app.MapIctStatusEndpoints();
app.MapIctSecretariesEndpoints();
app.MapIctLifecycleEndpoints();
app.MapIctObservabilityEndpoints();
app.MapIctTrazabilidadEndpoints();
app.MapIctClientAdminEndpoints();
// HU #14668 Bloque 2 — URL prefirmada del consolidado por referencia de trámite ICT.
app.MapIctDocumentsTransactionEndpoints();

// Servidor gRPC del callback de estados (core-api -> core-ict). Requiere HTTP/2 (h2c).
// Protegido con el service-token del canal inverso (aud=core-ict-internal, scope=ict.state): solo
// core-api autenticado como sistema puede notificar cambios de estado (no un tercero en el puerto interno).
app.MapGrpcService<IctStateCallbackService>()
    .RequireAuthorization(Flit.Ict.Api.Authorization.IctSecurityExtensions.CoreApiCallbackPolicy);

// Health del servicio (usado por el compose y el smoke local).
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "core-ict" }));

app.Run();

/// <summary>Expuesto para WebApplicationFactory en tests de integración.</summary>
public partial class Program;
