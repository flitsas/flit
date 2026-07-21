using Flit.Api.Authorization;
using Flit.Modules.Quipux.Application.UseCases.ConsultarLog;
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

        return app;
    }

    private static async Task<IResult> SearchAsync(
        [FromServices] ConsultarLogQuipuxHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] string? placa = null,
        [FromQuery] Guid? instanceId = null,
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
}
