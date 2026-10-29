using System.Globalization;
using Flit.PersonaNatural.Api.Abuso;
using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Sesiones;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Messaging;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.PersonaNatural.Api.Identidad;

/// <summary>
/// Validación de identidad de las partes de una solicitud (HU #14044 y #14045, ADR-0075). Rutas de la solicitud: exigen la
/// sesión de ESA solicitud (<see cref="SesionEndpoints.ConSesionDeLaSolicitud"/>) y el límite por IP; no llevan captcha (ya hay
/// sesión). Errores <c>application/problem+json</c> con un <c>code</c> estable y, cuando aplica, el <c>field</c> que falla.
/// </summary>
public static class IdentidadEndpoints
{
    /// <summary>Cola del consumidor del evento de FLIT 2 (<c>&lt;consumidor&gt;.&lt;propósito&gt;</c>, contrato §7).</summary>
    public const string Cola = "personanatural.identidad";

    /// <summary>Evento de FLIT 2 con el que una validación queda resuelta (<c>contracts/asyncapi/tramites-events.v1.yaml</c>).</summary>
    public const string EventoCompletada = "tramites.identity_validation.completed";

    /// <summary>Registra el servicio, el espejo, el job de reintento y el consumidor del evento de FLIT 2.</summary>
    public static IServiceCollection AddIdentidadPortal(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<IdentidadOptions>(configuration.GetSection(IdentidadOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IReglasDeIdentidad, ReglasDeIdentidadDesdeSnapshot>();
        services.AddScoped<EspejoDeIdentidad>();
        services.AddScoped<IdentidadService>();
        services.AddHostedService<ReintentoDeEnvioJob>();

        // El cliente gRPC de la fachada lo registra AddCotizacionesPortal cuando hay dirección de core-api; sin él, la
        // identidad responde 503 servicio_no_disponible (el servicio arranca igual).
        services.TryAddSingleton<IFachadaIdentidad>(sp => sp.GetService<Flit.Tramites.Grpc.V1.PersonaNaturalService.PersonaNaturalServiceClient>() is { } cliente
            ? new FachadaIdentidadGrpc(cliente)
            : new FachadaIdentidadNoConfigurada());

        // Espejo por evento: FLIT 2 publica tramites.identity_validation.completed en flit.tramites (con Tramites:Bus encendido).
        // La bandeja del SDK ignora un evento repetido; el sondeo de EstadoAsync y de la compuerta cubre el bus apagado.
        services.AddFlitConsumer<PersonaNaturalDb, ValidacionCompletadaConsumer, ValidacionCompletada>(
            configuration, Cola, producer: "tramites", [EventoCompletada]);
        return services;
    }

    public static IEndpointRouteBuilder MapIdentidadEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var grupo = app.MapGroup($"{SesionEndpoints.Base}/solicitudes/{{id:guid}}/identidad").AllowAnonymous().RequireRateLimiting(AbusoEndpoints.PoliticaPorIp);

        // HU #14045: estado de las validaciones de la solicitud (el front lo sondea).
        grupo.MapGet(string.Empty, async (Guid id, IdentidadService servicio, CancellationToken ct) =>
        {
            var estado = await servicio.EstadoAsync(id, ct).ConfigureAwait(false);
            return estado is null
                ? Problema(StatusCodes.Status404NotFound, "No encontrado", SesionEndpoints.MensajeNoEncontrada, SesionEndpoints.CodigoNoEncontrada)
                : Results.Ok(estado);
        }).AddEndpointFilter(Radicacion.SesionTrasRadicar.Filtro);

        // HU #14044: pide la validación de una parte. El cuerpo es opcional (la posición del actor si el rol admite varios).
        grupo.MapPost("/{parte}", async (Guid id, string parte, HttpContext http, IdentidadService servicio, CancellationToken ct, ParteRequest? body) =>
            Responder(http, await servicio.SolicitarAsync(id, parte, body?.Ordinal ?? 1, ct).ConfigureAwait(false)))
            .WithMetadata(Radicacion.PermiteTrasRadicar.Instancia) // HU #14049: ya rechaza sola (409 solicitud_no_editable) y no expone nada
            .AddEndpointFilter(Radicacion.SesionTrasRadicar.Filtro);

        // HU #14045: reenvía el enlace (opcionalmente a un correo corregido).
        grupo.MapPost("/{parte}/reenviar", async (Guid id, string parte, HttpContext http, IdentidadService servicio, CancellationToken ct, ReenviarRequest? body) =>
            Responder(http, await servicio.ReenviarAsync(id, parte, body?.Ordinal ?? 1, body?.Correo, ct).ConfigureAwait(false)))
            .WithMetadata(Radicacion.PermiteTrasRadicar.Instancia)
            .AddEndpointFilter(Radicacion.SesionTrasRadicar.Filtro);

        return app;
    }

    private static IResult Responder(HttpContext http, ResultadoIdentidad r)
    {
        if (r.Exito)
        {
            var cuerpo = new ParteResponse(r.Parte!, r.Ordinal, r.Estado!);
            return r.Aceptada ? Results.Accepted(value: cuerpo) : Results.Ok(cuerpo);
        }

        if (r.EsperaSegundos is { } espera)
            http.Response.Headers.RetryAfter = espera.ToString(CultureInfo.InvariantCulture);
        var extensiones = new Dictionary<string, object?> { ["code"] = r.Codigo };
        if (r.Estado is not null)
            extensiones["state"] = r.Estado;
        if (r.Campo is not null)
            extensiones["field"] = r.Campo;
        if (r.EsperaSegundos is { } s)
            extensiones["retryAfterSeconds"] = s;
        return Results.Problem(statusCode: r.Http, title: r.Codigo, detail: r.Mensaje, extensions: extensiones);
    }

    private static IResult Problema(int estado, string titulo, string detalle, string codigo) => SesionEndpoints.Problema(estado, titulo, detalle, codigo);
}

public sealed record ParteRequest(short? Ordinal);

public sealed record ReenviarRequest(short? Ordinal, string? Correo);

public sealed record ParteResponse(string Parte, short Ordinal, string Estado);

/// <summary>Datos del evento <c>tramites.identity_validation.completed</c> (sanitizados: solo ids y estado, sin datos personales).</summary>
public sealed record ValidacionCompletada(Guid ValidationId, string? Estado, string? Parte, Guid? ProcedureInstanceId, string? Provider, string? ProviderStatus, int? Score);

/// <summary>
/// Espejo por evento (HU #14045 AC4 y AC5): cuando FLIT 2 resuelve la validación, el estado de la parte pasa a aprobada o
/// rechazada y queda en <c>auditoria_identidad</c>. Solo atiende a la empresa de Persona Natural (el exchange <c>flit.tramites</c>
/// lleva los eventos de todas) y a las validaciones que el portal conoce. Un evento repetido lo ignora la bandeja del SDK y,
/// además, <see cref="EspejoDeIdentidad"/> es idempotente: el mismo estado no se audita dos veces.
/// </summary>
public sealed class ValidacionCompletadaConsumer(EspejoDeIdentidad espejo) : IEventConsumer<ValidacionCompletada>
{
    public async Task HandleAsync(EventEnvelope envelope, ValidacionCompletada data, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(data);
        if (envelope.TenantId != PlatformTenants.PersonaNatural)
            return;

        var estado = data.Estado switch
        {
            "aprobado" => EstadosIdentidad.Aprobada,
            "rechazado" => EstadosIdentidad.Rechazada,
            _ => null,
        };
        if (estado is null)
            return;

        // Sin una persona con ese id (el aviso llegó antes de que el BFF guardara el id, o no es del portal) no se pierde nada:
        // el sondeo de GET /identidad y la compuerta lo reconcilian contra FLIT 2.
        await espejo.AplicarAsync(data.ValidationId, estado, "evento", envelope.EventId, ct).ConfigureAwait(false);
    }
}

/// <summary>
/// Job periódico (in-process) que reintenta los envíos de enlace que quedaron en <c>pendiente_envio</c> porque FLIT 2 no
/// respondió. Un fallo no lo tumba. El envío es idempotente en FLIT 2, así que dos réplicas o un reintento tras una respuesta
/// perdida no duplican el enlace.
/// </summary>
public sealed partial class ReintentoDeEnvioJob(IServiceScopeFactory scopes, Microsoft.Extensions.Options.IOptions<IdentidadOptions> opciones, ILogger<ReintentoDeEnvioJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = opciones.Value;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(o.ArranqueSegundos), stoppingToken).ConfigureAwait(false);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var enviadas = await scope.ServiceProvider.GetRequiredService<IdentidadService>().ReintentarPendientesAsync(stoppingToken).ConfigureAwait(false);
                    if (enviadas > 0)
                        CicloTerminado(logger, enviadas);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    CicloFallido(logger, ex);
                }

                await Task.Delay(TimeSpan.FromSeconds(o.ReintentoSegundos), stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Apagado ordenado.
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Identidad: {Total} envíos pendientes reintentados")]
    private static partial void CicloTerminado(ILogger logger, int total);

    [LoggerMessage(Level = LogLevel.Error, Message = "Identidad: el reintento de envíos falló y se repite en el siguiente ciclo")]
    private static partial void CicloFallido(ILogger logger, Exception ex);
}
