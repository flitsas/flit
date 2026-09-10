using Flit.Api.Authorization;
using Flit.Modules.Quipux.Application.UseCases.ConsultarBandeja;
using Flit.Modules.Quipux.Application.UseCases.ConsultarLog;
using Flit.Modules.Quipux.Application.UseCases.ConsultarTrazabilidad;
using Flit.Modules.Quipux.Domain.LogQx;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// LOG QX (HU #10793): consulta de trazabilidad de la integración Quipux para soporte/administración.
/// Dada una placa, un id de trámite o un radicado, devuelve la(s) radicación(es) con su línea de
/// tiempo (<c>tramites.quipux_submission_events</c>) y el detalle técnico sanitizado de cada evento.
/// </summary>
/// <remarks>
/// Gate por permiso <c>logqx.read</c> (HU #10794): un usuario con ese permiso —o SuperAdmin, que hace
/// bypass— accede; cualquier otro autenticado recibe 403. Sin token, 401. El detalle técnico se
/// devuelve con los datos sensibles enmascarados (<see cref="ConsultarLogQuipuxHandler"/>).
/// </remarks>
public static class AdminLogQxEndpoints
{
    public static IEndpointRouteBuilder MapAdminLogQxEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/log-qx")
            .WithTags("Admin · LOG QX");

        // GET /api/v1/admin/log-qx?placa=|instanceId=|radicado= — búsqueda + timeline, paginada.
        group.MapGet("", SearchAsync)
            .RequirePermission("logqx.read")
            .WithName("AdminLogQxSearch")
            .WithSummary("Consulta el LOG QX de una radicación por placa, trámite o radicado")
            .WithDescription("Devuelve las radicaciones Quipux que casan con el filtro (placa, "
                + "instanceId o radicado), cada una con su línea de tiempo de eventos y el detalle "
                + "técnico sanitizado y enmascarado (duración, origen y código). Paginado "
                + "(page/pageSize). Requiere el permiso logqx.read (SuperAdmin bypassa). Sin filtro "
                + "lista todas las radicaciones paginadas.")
            .Produces<ConsultarLogQuipuxResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // GET /api/v1/admin/log-qx/bandeja — entrada del módulo: lista sin exigir búsqueda.
        group.MapGet("bandeja", BandejaAsync)
            .RequirePermission("logqx.read")
            .WithName("AdminLogQxBandeja")
            .WithSummary("Lista los trámites con integración Quipux, uno por fila")
            .WithDescription("Devuelve los trámites cuyo tipo declara integración Quipux —los que "
                + "ya tienen radicación y los elegibles que aún no se encolaron, estos últimos como "
                + "'sin_radicar'—, UNO POR TRÁMITE y no por radicación. Sin filtros responde el "
                + "periodo por defecto (últimos 30 días por última actividad), así que la pantalla "
                + "carga con datos sin buscar nada. Todos los filtros son combinables entre sí. "
                + "Incluye los contadores por estado sobre el conjunto filtrado completo. Requiere "
                + "el permiso logqx.read (SuperAdmin bypassa).")
            .Produces<ConsultarBandejaQuipuxResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // GET /api/v1/admin/log-qx/tipos — catálogo del desplegable «tipo de trámite».
        group.MapGet("tipos", TiposAsync)
            .RequirePermission("logqx.read")
            .WithName("AdminLogQxTipos")
            .WithSummary("Tipos de trámite que pueden aparecer en la bandeja")
            .WithDescription("Solo los tipos publicados CON homologación Quipux, que es el mismo "
                + "criterio con el que la bandeja arma su universo: ofrecer el catálogo completo "
                + "daría opciones que siempre devuelven cero. Cada uno viaja con su familia para "
                + "que el desplegable pueda agruparlos. Requiere el permiso logqx.read.")
            .Produces<IReadOnlyList<QuipuxTipoTramiteOpcion>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // GET /api/v1/admin/log-qx/{submissionId}/hitos — línea de tiempo con el sondeo agrupado.
        group.MapGet("{submissionId:guid}/hitos", HitosAsync)
            .RequirePermission("logqx.read")
            .WithName("AdminLogQxHitos")
            .WithSummary("Hitos de una radicación, con el sondeo repetido ya agrupado")
            .WithDescription("Devuelve la cabecera de la radicación —con sus radicaciones hermanas "
                + "para poder saltar entre intentos— y su línea de tiempo. Las consultas de estado "
                + "consecutivas que no cambiaron nada se colapsan EN SERVIDOR en un único bloque con "
                + "su conteo, ventana temporal y duración media, de modo que el payload no crece con "
                + "la antigüedad del trámite. Requiere el permiso logqx.read.")
            .Produces<ConsultarHitosQuipuxResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        // GET /api/v1/admin/log-qx/{submissionId}/eventos — log completo, filtrado y paginado.
        group.MapGet("{submissionId:guid}/eventos", EventosAsync)
            .RequirePermission("logqx.read")
            .WithName("AdminLogQxEventos")
            .WithSummary("Log completo de una radicación, filtrado y paginado en servidor")
            .WithDescription("Devuelve los eventos de la radicación con su detalle sanitizado y "
                + "enmascarado. Por defecto OCULTA las consultas de estado sin novedad e informa "
                + "cuántas ocultó; con ocultarSinNovedad=false devuelve la totalidad, sin perder "
                + "ningún registro. soloErrores deja únicamente los eventos cuyo resultado no es "
                + "correcto. Requiere el permiso logqx.read.")
            .Produces<ConsultarEventosQuipuxResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> SearchAsync(
        [FromServices] ConsultarLogQuipuxHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] string? placa = null,
        [FromQuery] string? instanceId = null,
        [FromQuery] string? radicado = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var result = await handler
            .HandleAsync(
                new ConsultarLogQuipuxQuery(placa, instanceId, radicado, page, pageSize),
                cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    /// <remarks>
    /// Los identificadores se enlazan como <c>string</c>, no como <c>Guid</c>: con <c>Guid</c> un
    /// valor mal tecleado revienta el binding con un 400 crudo, y esta es una pantalla de
    /// diagnóstico donde un filtro inválido debe devolver una lista vacía. El handler los parsea.
    /// </remarks>
    private static async Task<IResult> BandejaAsync(
        [FromServices] ConsultarBandejaQuipuxHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] DateTimeOffset? desde = null,
        [FromQuery] DateTimeOffset? hasta = null,
        [FromQuery] string? placa = null,
        [FromQuery] string? instanceId = null,
        [FromQuery] string? referencia = null,
        [FromQuery] string? documento = null,
        [FromQuery] string? estado = null,
        [FromQuery] string? transitOfficeId = null,
        [FromQuery] string? tenantId = null,
        [FromQuery] string? procedureTypeId = null,
        [FromQuery] string? familia = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var result = await handler
            .HandleAsync(
                new ConsultarBandejaQuipuxQuery(
                    desde, hasta, placa, instanceId, referencia, documento, estado,
                    transitOfficeId, tenantId, procedureTypeId, familia, page, pageSize),
                cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static async Task<IResult> TiposAsync(
        [FromServices] IQuipuxBandejaRepository repository,
        CancellationToken cancellationToken)
    {
        var tipos = await repository.ListProcedureTypesAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(tipos);
    }

    private static async Task<IResult> HitosAsync(
        [FromServices] ConsultarHitosQuipuxHandler handler,
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(submissionId, cancellationToken).ConfigureAwait(false);

        // 404 sin cuerpo: una radicación inexistente no debe filtrar nada de las demás.
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> EventosAsync(
        [FromServices] ConsultarEventosQuipuxHandler handler,
        Guid submissionId,
        CancellationToken cancellationToken,
        [FromQuery] bool? ocultarSinNovedad = null,
        [FromQuery] bool? soloErrores = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var result = await handler
            .HandleAsync(
                new ConsultarEventosQuipuxQuery(
                    submissionId, ocultarSinNovedad, soloErrores, page, pageSize),
                cancellationToken)
            .ConfigureAwait(false);

        return result is null ? Results.NotFound() : Results.Ok(result);
    }
}
