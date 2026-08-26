using Flit.Ict.Api.Authorization;
using Flit.Ict.Api.Endpoints;
using Flit.Ict.Api.Grpc;
using Flit.Ict.Application;
using Flit.Ict.Infrastructure;

// Permite gRPC sobre HTTP/2 en claro (h2c) hacia core-api en la red interna (sin TLS).
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddIctInfrastructure(builder.Configuration);
builder.Services.AddIctApplication();
builder.Services.AddIctApiSecurity(builder.Configuration);
builder.Services.AddGrpc();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
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
