using Flit.Api.Telemetry;
using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Abuso;
using Flit.PersonaNatural.Api.Adjuntos;
using Flit.PersonaNatural.Api.Consentimientos;
using Flit.PersonaNatural.Api.Consultas;
using Flit.PersonaNatural.Api.Identidad;
using Flit.PersonaNatural.Api.Definiciones;
using Flit.PersonaNatural.Api;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Pii;
using Flit.PersonaNatural.Api.Radicacion;
using Flit.PersonaNatural.Api.Radicados;
using Flit.PersonaNatural.Api.Sesiones;
using Flit.PersonaNatural.Api.Solicitudes;
using Flit.Platform.Sdk.Authentication;
using Flit.Platform.Sdk.Grpc;
using Flit.Platform.Sdk.Messaging;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.PersonaNatural.Api;

/// <summary>
/// core-personanatural (personanatural de servicio de plataforma, Epic #13316, HU #13340). REST en ASPNETCORE_URLS (solo salud por
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
        CaptchaOptions.Validar(builder.Configuration, builder.Environment.IsDevelopment());
        var grpcPort = builder.Configuration.GetValue<int>(ServicioSettings.GrpcPortKey);

        builder.Host.UseDefaultServiceProvider(o =>
        {
            o.ValidateScopes = true;
            o.ValidateOnBuild = true;
        });
        builder.AddFlitTelemetry($"flit-core-{ServicioSettings.Codigo}");

        var connectionString = builder.Configuration.GetConnectionString("Servicio")!;
        builder.Services.AddDbContext<PersonaNaturalDb>(o => PersonaNaturalDb.Configure(o, connectionString));
        builder.Services.AddFlitPlatformAuthentication(builder.Configuration);
        builder.Services.AddAuthorization();
        builder.Services.AddFlitGrpcServer().AddRadicacionGrpc(); // HU #14049: lectura por pull para radicar (token de svc-tramites)
        builder.Services.AddFlitOutbox<PersonaNaturalDb>(builder.Configuration);

        // HU #14035: sesión efímera por trámite (el portal no tiene cuentas) y huellas HMAC de la cédula y la IP.
        builder.Services.Configure<SesionOptions>(builder.Configuration.GetSection(SesionOptions.SectionName));
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<PiiHasher>();
        builder.Services.AddScoped<SesionService>();

        // HU #14037: cifrado AES-GCM de los datos personales, auditoría de lectura y retención de borradores abandonados.
        builder.Services.AddSingleton<CifradoPii>();
        builder.Services.AddScoped<PersonaService>();
        builder.Services.AddScoped<RetencionService>();
        builder.Services.AddHostedService<RetencionJob>();

        // HU #14036: captcha, límite por IP y topes de consultas por cédula y globales.
        builder.Services.AddAbusoPortal(builder.Configuration);

        // HU #14041/#14047: versiones vigentes del texto de consentimiento y constancia de las consultas públicas.
        builder.Services.AddConsentimientosPortal();

        // HU #14042: consulta pública de radicado con macro-estados.
        builder.Services.AddRadicadosPortal();

        // HU #14039 y #14040: consultas de vehículo y conductor a core-consultas, proyectadas a dato mínimo.
        builder.Services.AddConsultasPortal(builder.Configuration);

        // HU #14041: endpoints públicos de consulta, detalle ampliado con identidad validada y medición de consumo.
        builder.Services.AddConsultasPublicas();

        // HU #14043: catálogo de organismos y tipos, cotización de 2 líneas y envío por correo (fachada gRPC de core-api).
        builder.Services.AddCotizacionesPortal(builder.Configuration);

        // HU #14044 y #14045: validación de identidad de las partes por enlace (fachada gRPC de core-api, espejo por evento).
        builder.Services.AddIdentidadPortal(builder.Configuration);

        // HU #14050 y #14051: espejo de lo que core-api resuelve (trámite creado o rechazado) y de los cambios de estado del trámite.
        builder.Services.AddEstadoDeVueltaPortal(builder.Configuration);

        // HU #14078: definición del tipo de trámite (estructura del wizard) desde la fachada de core-api.
        builder.Services.AddDefinicionesPortal();

        // HU #14047: borrador del trámite (sesión por solicitud, actores, campos, comercial, requisitos y cancelación).
        builder.Services.AddSolicitudesPortal();

        // HU #14048: archivos del trámite y comprobante de pago (file-manager corporativo; sin él, 503 almacenamiento_no_disponible).
        builder.Services.AddAdjuntosPortal(builder.Configuration);

        // HU #14049: radicar con outbox e Idempotency-Key (con la compuerta de identidad de la HU #14046: una sola implementación).
        builder.Services.AddRadicacionPortal();

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
        app.UseAntiAbusoHttp(); // HU #14036: errores sin detalle, IP real por saltos de confianza y tope de cuerpo
        app.UseFlitCorrelationId();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapSesionEndpoints();
        app.MapRadicadoEndpoints();
        app.MapConsultasEndpoints();

        app.MapCotizacionesEndpoints();
        app.MapIdentidadEndpoints();
        app.MapDefinicionesEndpoints();
        app.MapSolicitudesEndpoints();
        app.MapAdjuntosEndpoints();
        app.MapRadicacionEndpoints();
        app.MapGet("/health", () => Results.Ok(new { status = "alive" })).AllowAnonymous();
        app.MapGet("/health/ready", async (PersonaNaturalDb db, CancellationToken ct) =>
            await db.Database.CanConnectAsync(ct).ConfigureAwait(false)
            && !(await db.Database.GetPendingMigrationsAsync(ct).ConfigureAwait(false)).Any()
                ? Results.Ok(new { status = "ready" })
                : Results.Json(new { status = "not_ready" }, statusCode: StatusCodes.Status503ServiceUnavailable)).AllowAnonymous();

        // gRPC solo en su puerto: los servicios gRPC del dominio se mapean aquí con .RequireHost(grpcHost).
        var grpcHost = $"*:{grpcPort}";
        app.MapFlitGrpcPlatform(app.Environment, grpcHost);
        app.MapRadicacionGrpc(grpcHost);
        return app;
    }

    /// <summary>Aplica las migraciones pendientes del esquema del servicio (EF las serializa entre réplicas).</summary>
    internal static async Task MigrateAsync(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (!app.Configuration.GetValue("Servicio:MigrateOnStartup", true))
            return;

        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PersonaNaturalDb>().Database.MigrateAsync().ConfigureAwait(false);
    }
}

/// <summary>Tipo de referencia del ensamblado para <c>WebApplicationFactory</c> (Program es estático).</summary>
public sealed class PersonaNaturalApiEntryPoint;
