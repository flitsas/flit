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
builder.Services.AddIctApiSecurity();
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

// Logging de requests entrantes a /api/ict/** (redactado, en scope propio). Tras la auth para
// resolver el tenant del token ICT.
app.UseMiddleware<Flit.Ict.Api.Middleware.IctRequestLoggingMiddleware>();

app.MapIctAuthEndpoints();
app.MapIctRegisterEndpoints();
app.MapIctPreTramiteEndpoints();
app.MapIctAttachmentEndpoints();
app.MapIctStatusEndpoints();
app.MapIctObservabilityEndpoints();

// Servidor gRPC del callback de estados (core-api -> core-ict). Requiere HTTP/2 (h2c en dev).
app.MapGrpcService<IctStateCallbackService>();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "core-ict" }));

app.Run();

/// <summary>Expuesto para WebApplicationFactory en tests de integración.</summary>
public partial class Program;
