using Flit.Ict.Api.Authorization;
using Flit.Ict.Api.Endpoints;
using Flit.Ict.Api.Grpc;
using Flit.Ict.Application;
using Flit.Ict.Infrastructure;

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
builder.Services.AddGrpc();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// HU #12895 (FLIT Suite A-02): Swagger:Enabled lo decide, por defecto solo en Development (igual que core-api).
if (app.Configuration.GetValue("Swagger:Enabled", app.Environment.IsDevelopment()))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
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

// Servidor gRPC del callback de estados (core-api -> core-ict). Requiere HTTP/2 (h2c en dev).
// Protegido con el service-token del canal inverso (aud=core-ict-internal, scope=ict.state): solo
// core-api autenticado como sistema puede notificar cambios de estado (no un tercero en el puerto interno).
app.MapGrpcService<IctStateCallbackService>()
    .RequireAuthorization(Flit.Ict.Api.Authorization.IctSecurityExtensions.CoreApiCallbackPolicy);

// Health del servicio (usado por el compose y el smoke local).
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "core-ict" }));

app.Run();

/// <summary>Expuesto para WebApplicationFactory en tests de integración.</summary>
public partial class Program;
