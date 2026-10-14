using Flit.Api.Telemetry;
using Flit.Consultas.Api.Avisos;
using Flit.Consultas.Api.Configuracion;
using Flit.Consultas.Api.Grpc;
using Flit.Infrastructure.Consultations.Avaluos;
using Flit.Modules.Consultas;
using Flit.Modules.Consultas.KyverumVerify;
using Flit.Tramites.Application.UseCases.Avaluos;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Consultas.Api;
using Flit.Consultas.Api.Persistence;
using Flit.Platform.Sdk.Authentication;
using Flit.Platform.Sdk.Grpc;
using Flit.Platform.Sdk.Messaging;
using Microsoft.AspNetCore.DataProtection;
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

    /// <param name="reemplazos">Solo pruebas: corre después de todo el registro, para reemplazar un servicio.</param>
    internal static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null, Action<IServiceCollection>? reemplazos = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);
        ServicioSettings.Validate(builder.Configuration);
        ProveedoresSettings.Validate(builder.Configuration); // HU #13347: modo real sin su secreto no arranca
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
            .RequireServiceToken<ConsultasAdminGrpcService>(ConsultasAdminGrpcService.Scope, Flit.Api.Identity.ServiceAudiences.Consultas)
            .RequireServiceToken<ValidacionIdentidadGrpcService>(ValidacionIdentidadGrpcService.Scope, Flit.Api.Identity.ServiceAudiences.Consultas);
        builder.Services.AddFlitOutbox<ConsultasDb>(builder.Configuration);

        // HU #13343 (ADR-0065): los proveedores del módulo, con sus modos mock|real y credenciales (las mismas claves de
        // configuración que core-api), y los puentes sobre el esquema propio.
        builder.Services.TryAddSingleton(TimeProvider.System);
        ConsultasModuleExtensions.ConfigureKyverumRunt(builder.Services, builder.Configuration);
        builder.Services.AddConsultationProviders(builder.Configuration);
        builder.Services.AddClientesDeDocumentos(builder.Configuration); // HU #13348: impronta, RUES y RUNT crudo
        builder.Services.AddScoped<IConsultationTenantOverrideProvider, ConsultasTenantOverrideProvider>();
        builder.Services.AddScoped<IAvaluoProviderPolicy, ConsultasAvaluoPolicy>();
        builder.Services.AddScoped<IAvaluoMockValueSource, ValoresMockDeAvaluo>(); // HU #13348
        builder.Services.AddScoped<ConsumoRecorder>(); // HU #13345

        // HU #13351 (ADR-0065 §6-7): Kyverum Verify por Consultas. Mismas variables que core-api (la de entorno primero);
        // KYVERUM_WEBHOOK_CALLBACK_URL apunta al receptor de Consultas (AvisosKyverumEndpoints.Ruta sin el id). Las llaves
        // de Data Protection que cifran los secretos de los avisos viven en el esquema propio.
        string? EnvPrimero(string key, string env) =>
            Environment.GetEnvironmentVariable(env) is { Length: > 0 } v ? v : builder.Configuration[key];
        builder.Services.AddKyverumVerifyClients(o =>
        {
            o.BaseUrl = EnvPrimero("Kyverum:BaseUrl", "KYVERUM_BASE_URL") ?? "https://verify.kyverum.com";
            o.ApiKey = EnvPrimero("Kyverum:ApiKey", "KYVERUM_API_KEY") ?? "";
            o.AuthScheme = EnvPrimero("Kyverum:AuthScheme", "KYVERUM_AUTH_SCHEME") ?? "Bearer";
            o.TimeoutSeconds = int.TryParse(EnvPrimero("Kyverum:TimeoutSeconds", "KYVERUM_TIMEOUT_SECONDS"), out var t) ? t : 30;
            o.WebhookCallbackUrl = EnvPrimero("Kyverum:WebhookCallbackUrl", "KYVERUM_WEBHOOK_CALLBACK_URL") ?? "";
        });
        builder.Services.AddDataProtection()
            .PersistKeysToDbContext<ConsultasDb>()
            .SetApplicationName($"flit-core-{ServicioSettings.Codigo}");

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

        reemplazos?.Invoke(builder.Services);
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
        app.MapGrpcService<ValidacionIdentidadGrpcService>().RequireHost(grpcHost);
        app.MapAvisosKyverum(); // HU #13351: receptor público de avisos (lo expone el gateway)
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
