using System.Text.Json.Serialization;
using Flit.Api.Telemetry;
using Flit.Comparendos.Api;
using Flit.Comparendos.Api.Infraestructura.Archivos;
using Flit.Comparendos.Api.Modulos;
using Flit.Comparendos.Api.Modulos.Calendario;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Plataforma;
using Flit.Platform.Sdk.Authentication;
using Flit.Platform.Sdk.Grpc;
using Flit.Platform.Sdk.Messaging;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Comparendos.Api;

/// <summary>
/// core-comparendos: Gestión de Comparendos de la FLIT Suite (Epic #12878). REST en ASPNETCORE_URLS bajo
/// <c>/api/v1/comparendos</c> (la app web llega por el gateway con el token del producto), gRPC en
/// <see cref="ServicioSettings.GrpcPortKey"/> (red interna) y bus por la outbox/bandeja del SDK. Al arrancar aplica sus
/// migraciones (solo su esquema), siembra los datos de arranque y registra su manifiesto de permisos.
/// </summary>
public static class Program
{
    public const string Prefijo = "/api/v1/comparendos";

    public static async Task Main(string[] args)
    {
        var app = Build(args);
        await MigrateAsync(app).ConfigureAwait(false);
        await app.RunAsync().ConfigureAwait(false);
    }

    /// <param name="reemplazos">Pruebas: cambia servicios después de registrarlos todos (fuentes, correo, reloj).</param>
    internal static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null, Action<IServiceCollection>? reemplazos = null)
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
        builder.Services.AddDbContext<ComparendosDb>(o => ComparendosDb.Configure(o, connectionString));
        builder.Services.AddFlitPlatformAuthentication(builder.Configuration);
        builder.Services.AddAutorizacionComparendos();
        builder.Services.AddFlitGrpcServer();
        builder.Services.AddFlitOutbox<ComparendosDb>(builder.Configuration);
        AddTokensDeServicio(builder.Services, builder.Configuration);

        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        });
        builder.Services.AddExceptionHandler<ManejadorReglas>();
        builder.Services.AddProblemDetails();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<Reloj>();
        builder.Services.AddSingleton<CalendarioService>();
        builder.Services.AddScoped<Capacidades>();
        builder.Services.AddAlmacenArchivos(builder.Configuration);
        builder.Services.AddModulosComparendos(builder.Configuration, builder.Environment);
        builder.Services.AddHttpClient(RegistroManifiesto.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(20));
        if (builder.Configuration.GetValue("Comparendos:Plataforma:RegistrarManifiesto", true))
            builder.Services.AddHostedService<RegistroManifiesto>();
        reemplazos?.Invoke(builder.Services);

        // h2c necesita un endpoint solo HTTP/2: se vuelven a declarar las URLs del REST (Kestrel ignora ASPNETCORE_URLS
        // cuando se declaran endpoints por código) y se suma el del gRPC.
        builder.WebHost.ConfigureKestrel((context, options) =>
        {
            options.Limits.MaxRequestBodySize = 512L * 1024 * 1024;
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

        app.MapGet("/health", () => Results.Ok(new { status = "alive" })).AllowAnonymous();
        app.MapGet("/health/ready", async (ComparendosDb db, CancellationToken ct) =>
            await db.Database.CanConnectAsync(ct).ConfigureAwait(false)
            && !(await db.Database.GetPendingMigrationsAsync(ct).ConfigureAwait(false)).Any()
                ? Results.Ok(new { status = "ready" })
                : Results.Json(new { status = "not_ready" }, statusCode: StatusCodes.Status503ServiceUnavailable)).AllowAnonymous();

        app.MapModulosComparendos(Prefijo);

        // gRPC solo en su puerto: los servicios gRPC del dominio se mapean aquí con .RequireHost(grpcHost).
        var grpcHost = $"*:{grpcPort}";
        app.MapFlitGrpcPlatform(app.Environment, grpcHost);
        return app;
    }

    /// <summary>Aplica las migraciones pendientes del esquema del servicio (EF las serializa entre réplicas) y siembra.</summary>
    internal static async Task MigrateAsync(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (!app.Configuration.GetValue("Servicio:MigrateOnStartup", true))
            return;

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComparendosDb>();
        await db.Database.MigrateAsync().ConfigureAwait(false);
        await Semillas.Semillas.SembrarAsync(db, scope.ServiceProvider.GetRequiredService<TimeProvider>(), CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Tokens de servicio de svc-comparendos (mismo registro que hace el SDK para sus clientes gRPC).</summary>
    private static void AddTokensDeServicio(IServiceCollection services, ConfigurationManager configuration)
    {
        services.Configure<PlatformServiceClientOptions>(configuration.GetSection(PlatformServiceClientOptions.SectionName));
        services.AddHttpClient(ServiceTokenProvider.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ServiceTokenProvider>();
    }
}

/// <summary>Tipo de referencia del ensamblado para <c>WebApplicationFactory</c> (Program es estático).</summary>
public sealed class ComparendosApiEntryPoint;
