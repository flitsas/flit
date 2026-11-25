using Flit.Api.Telemetry;
using Flit.Impuestos.Api;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Platform.Sdk.Authentication;
using Flit.Platform.Sdk.Grpc;
using Flit.Platform.Sdk.Messaging;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Impuestos.Api;

/// <summary>
/// core-impuestos: producto Impuestos de la FLIT Suite (HU #14694, generado con la plantilla flit-servicio; ADR-0093). REST en
/// ASPNETCORE_URLS bajo <see cref="Prefijo"/> (la app web llega por el gateway con el token del producto), gRPC en
/// <see cref="ServicioSettings.GrpcPortKey"/> (red interna, solo HTTP/2) y bus por la outbox/bandeja del SDK. Al arrancar aplica
/// sus migraciones (solo su esquema) y registra su manifiesto de permisos en la plataforma.
/// </summary>
public static class Program
{
    public const string Prefijo = "/api/v1/impuestos";

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
        builder.Services.AddDbContext<ImpuestosDb>(o => ImpuestosDb.Configure(o, connectionString));
        builder.Services.AddFlitPlatformAuthentication(builder.Configuration);
        builder.Services.AddAutorizacionImpuestos();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddScoped<Modulos.Comun.AuditoriaLecturaBackoffice>();
        // Plan §4.2: Data Protection propio con el anillo en la base cifrado con el certificado de la configuración (fail-closed).
        builder.Services.AddProteccionDatos(builder.Configuration);
        // EP-CF3a: ningún HttpClient del servicio registra el valor de X-API-Key, Authorization ni X-Flit-Signature.
        builder.Services.AddRedaccionCabeceras();
        builder.Services.AddSuscripcionImpuestos(builder.Configuration);
        builder.Services.AddLimitesSolicitudes(builder.Configuration);
        builder.Services.AddFlitGrpcServer();
        builder.Services.AddFlitOutbox<ImpuestosDb>(builder.Configuration);
        builder.Services.AddExceptionHandler<ManejadorReglas>();
        builder.Services.AddProblemDetails();
        AddTokensDeServicio(builder.Services, builder.Configuration);
        builder.Services.AddHttpClient(RegistroManifiesto.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(20));
        if (builder.Configuration.GetValue(RegistroManifiesto.RegistrarKey, true))
            builder.Services.AddHostedService<RegistroManifiesto>();

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
        app.UseExceptionHandler();
        app.UseFlitCorrelationId();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        // Toda ruta de negocio verifica la suscripción de la compañía del token (fail-closed); /health* queda fuera.
        app.UseGuardSuscripcion(Prefijo);

        app.MapGet("/health", () => Results.Ok(new { status = "alive" })).AllowAnonymous();
        app.MapGet("/health/ready", async (ImpuestosDb db, CancellationToken ct) =>
            await db.Database.CanConnectAsync(ct).ConfigureAwait(false)
            && !(await db.Database.GetPendingMigrationsAsync(ct).ConfigureAwait(false)).Any()
                ? Results.Ok(new { status = "ready" })
                : Results.Json(new { status = "not_ready" }, statusCode: StatusCodes.Status503ServiceUnavailable)).AllowAnonymous();

        // Rutas de negocio: todo lo que cuelga del prefijo exige un token del producto (audiencia impuestos). Cada módulo suma
        // aquí su Map; lo que no existe responde 404 a quien sí está autenticado (el fallback nunca tapa una ruta mapeada).
        var api = app.MapGroup(Prefijo).RequireAuthorization();
        Modulos.Sesion.SesionEndpoints.Map(api);
        api.MapFallback(() => Results.NotFound());

        // gRPC solo en su puerto: los servicios gRPC del dominio se mapean aquí con .RequireHost(grpcHost).
        var grpcHost = $"*:{grpcPort}";
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
        await scope.ServiceProvider.GetRequiredService<ImpuestosDb>().Database.MigrateAsync().ConfigureAwait(false);
    }

    /// <summary>Tokens de servicio de svc-impuestos para el manifiesto (mismo registro que hace el SDK para sus clientes gRPC).</summary>
    private static void AddTokensDeServicio(IServiceCollection services, ConfigurationManager configuration)
    {
        services.Configure<PlatformServiceClientOptions>(configuration.GetSection(PlatformServiceClientOptions.SectionName));
        services.AddHttpClient(ServiceTokenProvider.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
        services.TryAddSingleton<ServiceTokenProvider>();
    }
}

/// <summary>Tipo de referencia del ensamblado para <c>WebApplicationFactory</c> (Program es estático).</summary>
public sealed class ImpuestosApiEntryPoint;
