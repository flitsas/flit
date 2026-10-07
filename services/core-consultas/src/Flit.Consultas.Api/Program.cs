using Flit.Api.Telemetry;
using Flit.Consultas.Api.Configuracion;
using Flit.Consultas.Api.Grpc;
using Flit.Infrastructure.Consultations.Avaluos;
using Flit.Modules.Consultas;
using Flit.Tramites.Application.UseCases.Avaluos;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Consultas.Api;
using Flit.Consultas.Api.Persistence;
using Flit.Platform.Sdk.Authentication;
using Flit.Platform.Sdk.Grpc;
using Flit.Platform.Sdk.Messaging;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Consultas.Api;

/// <summary>
/// core-consultas (consultas de servicio de plataforma, Epic #13316, HU #13340). REST en ASPNETCORE_URLS (solo salud por
/// ahora; lo que entra desde fuera de FLIT va por el gateway), gRPC en <see cref="ServicioSettings.GrpcPortKey"/> (red
/// interna, solo HTTP/2) y bus por la outbox/bandeja del SDK. Al arrancar aplica sus migraciones (solo su esquema).
/// </summary>
public static class Program
{
    public static async Task Main(string[] args)
    {
        var app = Build(args);
        await MigrateAsync(app).ConfigureAwait(false);
        await app.RunAsync().ConfigureAwait(false);
    }

    internal static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);
        ServicioSettings.Validate(builder.Configuration);
        var grpcPort = builder.Configuration.GetValue<int>(ServicioSettings.GrpcPortKey);

        builder.Host.UseDefaultServiceProvider(o =>
        {
            o.ValidateScopes = true;
            o.ValidateOnBuild = true;
        });
        builder.AddFlitTelemetry($"flit-core-{ServicioSettings.Codigo}");

        var connectionString = builder.Configuration.GetConnectionString("Servicio")!;
        builder.Services.AddDbContext<ConsultasDb>(o => ConsultasDb.Configure(o, connectionString));
        builder.Services.AddFlitPlatformAuthentication(builder.Configuration);
        builder.Services.AddAuthorization();
        builder.Services.AddFlitGrpcServer()
            .RequireServiceToken<ConsultasGrpcService>(ConsultasGrpcService.Scope, Flit.Api.Identity.ServiceAudiences.Consultas)
            .RequireServiceToken<ConsultasAdminGrpcService>(ConsultasAdminGrpcService.Scope, Flit.Api.Identity.ServiceAudiences.Consultas);
        builder.Services.AddFlitOutbox<ConsultasDb>(builder.Configuration);

        // HU #13343 (ADR-0065): los proveedores del módulo, con sus modos mock|real y credenciales (las mismas claves de
        // configuración que core-api), y los puentes sobre el esquema propio.
        builder.Services.TryAddSingleton(TimeProvider.System);
        ConsultasModuleExtensions.ConfigureKyverumRunt(builder.Services, builder.Configuration);
        builder.Services.AddConsultationProviders(builder.Configuration);
        builder.Services.AddScoped<IConsultationTenantOverrideProvider, ConsultasTenantOverrideProvider>();
        builder.Services.AddScoped<IAvaluoProviderPolicy, ConsultasAvaluoPolicy>();
        builder.Services.AddSingleton<IAvaluoMockValueSource, SinValoresMockDeAvaluo>();
        builder.Services.AddScoped<ConsumoRecorder>(); // HU #13345

        // h2c necesita un endpoint solo HTTP/2: se vuelven a declarar las URLs del REST (Kestrel ignora ASPNETCORE_URLS
        // cuando se declaran endpoints por código) y se suma el del gRPC.
        builder.WebHost.ConfigureKestrel((context, options) =>
        {
            foreach (var raw in (context.Configuration[WebHostDefaults.ServerUrlsKey] ?? "http://localhost:5000")
                         .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var uri = new Uri(raw.Replace("://+", "://0.0.0.0", StringComparison.Ordinal).Replace("://*", "://0.0.0.0", StringComparison.Ordinal));
                if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
                    options.ListenLocalhost(uri.Port, lo => lo.Protocols = HttpProtocols.Http1AndHttp2);
                else
                    options.ListenAnyIP(uri.Port, lo => lo.Protocols = HttpProtocols.Http1AndHttp2);
            }

            options.ListenAnyIP(grpcPort, lo => lo.Protocols = HttpProtocols.Http2);
        });

        var app = builder.Build();
        app.UseFlitCorrelationId();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/health", () => Results.Ok(new { status = "alive" })).AllowAnonymous();
        app.MapGet("/health/ready", async (ConsultasDb db, CancellationToken ct) =>
            await db.Database.CanConnectAsync(ct).ConfigureAwait(false)
            && !(await db.Database.GetPendingMigrationsAsync(ct).ConfigureAwait(false)).Any()
                ? Results.Ok(new { status = "ready" })
                : Results.Json(new { status = "not_ready" }, statusCode: StatusCodes.Status503ServiceUnavailable)).AllowAnonymous();

        // gRPC solo en su puerto: los servicios gRPC del dominio se mapean aquí con .RequireHost(grpcHost).
        var grpcHost = $"*:{grpcPort}";
        app.MapGrpcService<ConsultasGrpcService>().RequireHost(grpcHost);
        app.MapGrpcService<ConsultasAdminGrpcService>().RequireHost(grpcHost);
        app.MapFlitGrpcPlatform(app.Environment, grpcHost);
        return app;
    }

    /// <summary>Aplica las migraciones pendientes del esquema del servicio (EF las serializa entre réplicas).</summary>
    internal static async Task MigrateAsync(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (!app.Configuration.GetValue("Servicio:MigrateOnStartup", true))
            return;

        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ConsultasDb>().Database.MigrateAsync().ConfigureAwait(false);
    }
}

/// <summary>Tipo de referencia del ensamblado para <c>WebApplicationFactory</c> (Program es estático).</summary>
public sealed class ConsultasApiEntryPoint;
