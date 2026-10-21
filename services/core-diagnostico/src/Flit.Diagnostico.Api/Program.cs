using Flit.Api.Telemetry;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Platform.Sdk.Authentication;
using Flit.Platform.Sdk.Grpc;
using Flit.Platform.Sdk.Messaging;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Diagnostico.Api;

/// <summary>
/// core-diagnostico: Diagnóstico Vehicular de la FLIT Suite (HU #13721, generado con la plantilla flit-servicio). REST en
/// ASPNETCORE_URLS bajo <see cref="Prefijo"/> (la app web llega por el gateway con el token del producto), gRPC en
/// <see cref="ServicioSettings.GrpcPortKey"/> (red interna, solo HTTP/2) y bus por la outbox/bandeja del SDK. Al arrancar
/// aplica sus migraciones (solo su esquema) y registra su manifiesto de permisos en la plataforma (HU #13724).
/// </summary>
public static class Program
{
    public const string Prefijo = "/api/v1/diagnostico";

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
        builder.Services.AddDbContext<DiagnosticoDb>(o => DiagnosticoDb.Configure(o, connectionString));
        builder.Services.AddFlitPlatformAuthentication(builder.Configuration);
        builder.Services.AddAutorizacionDiagnostico();
        // HU #13728: auditoría de cada lectura entre compañías del backoffice (diagnostico.lecturas_backoffice).
        builder.Services.AddScoped<Modulos.Comun.AuditoriaLecturaBackoffice>();
        // HU #13741: configuración en dos niveles (operativa de la compañía y contrato del backoffice).
        builder.Services.AddSingleton<Modulos.Configuracion.DisponibilidadFuentes>();
        builder.Services.AddScoped<Modulos.Configuracion.ServicioConfiguracion>();
        builder.Services.AddSuscripcionDiagnostico(builder.Configuration);
        // HU #13742: catálogo de tipos de trámite por el gRPC de catálogos de core-api (token de svc-diagnostico).
        Modulos.Catalogos.TiposTramiteEndpoints.AddCatalogoTiposTramite(builder.Services, builder.Configuration);
        // HU #13750: crear y editar la cabecera del diagnóstico.
        builder.Services.AddScoped<Modulos.Diagnosticos.ServicioCabecera>();
        // HU #13751: Data Protection con el anillo en la base cifrado con el certificado de la configuración (fail-closed),
        // confirmación de los vehículos del lote y el puerto al motor de consultas (en D1 no hace nada; #13719 lo reemplaza).
        builder.Services.AddProteccionDocumentos(builder.Configuration);
        builder.Services.AddScoped<Modulos.Vehiculos.ServicioConfirmacionVehiculos>();
        builder.Services.TryAddScoped<Domain.Consultas.IEncoladorConsultas, Modulos.Consultas.EncoladorConsultasNulo>();
        // HU #13753: Diagnóstico Express (cabecera + un vehículo en una transacción) y su estimado de vista previa.
        builder.Services.AddScoped<Modulos.Express.ServicioExpress>();
        // HU #13725: fuente RUNT del vehículo (no conectada por defecto; simulada de Intempo solo con configuración explícita en LOCAL o DEV).
        builder.Services.AddFuentesConsultas(builder.Configuration);
        builder.Services.AddFlitGrpcServer();
        builder.Services.AddFlitOutbox<DiagnosticoDb>(builder.Configuration);
        builder.Services.AddExceptionHandler<ManejadorReglas>();
        builder.Services.AddProblemDetails();
        AddTokensDeServicio(builder.Services, builder.Configuration);
        builder.Services.AddHttpClient(RegistroManifiesto.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(20));
        if (builder.Configuration.GetValue("Diagnostico:Plataforma:RegistrarManifiesto", true))
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
        // HU #13726: toda ruta de negocio verifica la suscripción de la compañía del token (fail-closed); /health* queda fuera.
        app.UseGuardSuscripcion(Prefijo);

        app.MapGet("/health", () => Results.Ok(new { status = "alive" })).AllowAnonymous();
        app.MapGet("/health/ready", async (DiagnosticoDb db, CancellationToken ct) =>
            await db.Database.CanConnectAsync(ct).ConfigureAwait(false)
            && !(await db.Database.GetPendingMigrationsAsync(ct).ConfigureAwait(false)).Any()
                ? Results.Ok(new { status = "ready" })
                : Results.Json(new { status = "not_ready" }, statusCode: StatusCodes.Status503ServiceUnavailable)).AllowAnonymous();

        // Rutas de negocio: todo lo que cuelga del prefijo exige un token del producto (audiencia diagnostico). Cada módulo
        // se mapea en Modulos.MapModulosDiagnostico; lo que todavía no existe responde 404 a quien sí está autenticado.
        app.MapModulosDiagnostico(Prefijo);

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
        await scope.ServiceProvider.GetRequiredService<DiagnosticoDb>().Database.MigrateAsync().ConfigureAwait(false);
    }

    /// <summary>Tokens de servicio de svc-diagnostico para el manifiesto (mismo registro que hace el SDK para sus clientes gRPC).</summary>
    private static void AddTokensDeServicio(IServiceCollection services, ConfigurationManager configuration)
    {
        services.Configure<PlatformServiceClientOptions>(configuration.GetSection(PlatformServiceClientOptions.SectionName));
        services.AddHttpClient(ServiceTokenProvider.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ServiceTokenProvider>();
    }
}

/// <summary>Tipo de referencia del ensamblado para <c>WebApplicationFactory</c> (Program es estático).</summary>
public sealed class DiagnosticoApiEntryPoint;
