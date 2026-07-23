using Flit.Ict.Api.Authorization;
using Flit.Ict.Api.Endpoints;
using Flit.Ict.Api.Grpc;
using Flit.Ict.Application;
using Flit.Ict.Infrastructure;

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

app.MapIctAuthEndpoints();

// Servidor gRPC del callback de estados (core-api -> core-ict). Requiere HTTP/2 (h2c en dev).
app.MapGrpcService<IctStateCallbackService>();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "core-ict" }));

app.Run();

/// <summary>Expuesto para WebApplicationFactory en tests de integración.</summary>
public partial class Program;
