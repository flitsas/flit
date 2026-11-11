using Flit.Admin.Application.Plataforma.PersonaNatural;
using Flit.Admin.Domain.Plataforma.PersonaNatural;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Reportes del portal en Plataforma (HU #14397 y #14398, Feature #14396, Épica #14297): <c>/reportes</c> y <c>/reportes/export</c>.
/// El SuperAdmin ve web, punto y aplicación de todos los organismos y puede filtrar por <c>transitOfficeId</c>. Hereda la policy y el
/// permiso del grupo. Comparte consulta, filtros y errores con la vista del organismo.
/// </summary>
public static partial class AdminPlataformaPersonaNaturalEndpoints
{
    private static readonly AlcanceReportePortal AlcancePlataforma = new(Plataforma: true, OtTenantId: null, Organismos: []);

    private static void MapReportes(RouteGroupBuilder group)
    {
        group.MapGet("/reportes", ListarReportesAsync)
            .WithName("AdminPnReportesList")
            .WithSummary("Trámites del portal que cayeron a FLIT 2.0, de todos los organismos, con filtros y paginación")
            .Produces<PaginaReportePortal>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/reportes/export", ExportarReportesAsync)
            .WithName("AdminPnReportesExport")
            .WithSummary("Excel del mismo conjunto filtrado del reporte del portal")
            .Produces(StatusCodes.Status200OK, contentType: AdminOtPortalReportesEndpoints.ExcelContentType)
            .Produces(StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> ListarReportesAsync(
        [FromServices] ListarReportePortalHandler handler,
        CancellationToken ct,
        [FromQuery] string? canal = null,
        [FromQuery] string? tipoTramiteId = null,
        [FromQuery] DateOnly? desde = null,
        [FromQuery] DateOnly? hasta = null,
        [FromQuery] string? transitOfficeId = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var consulta = ReportePortalRespuestas.Leer(canal, tipoTramiteId, transitOfficeId, desde, hasta);
        if (consulta.Falla is not null)
            return consulta.Falla;

        var r = await handler.HandleAsync(AlcancePlataforma with { Organismos = consulta.Organismos }, consulta.Solicitud!, page, pageSize, ct)
            .ConfigureAwait(false);
        return r.Error is null ? Results.Ok(r.Pagina) : ReportePortalRespuestas.Falla(r.Error);
    }

    private static async Task<IResult> ExportarReportesAsync(
        [FromServices] ExportarReportePortalHandler handler,
        CancellationToken ct,
        [FromQuery] string? canal = null,
        [FromQuery] string? tipoTramiteId = null,
        [FromQuery] DateOnly? desde = null,
        [FromQuery] DateOnly? hasta = null,
        [FromQuery] string? transitOfficeId = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var consulta = ReportePortalRespuestas.Leer(canal, tipoTramiteId, transitOfficeId, desde, hasta);
        if (consulta.Falla is not null)
            return consulta.Falla;

        return await ReportePortalRespuestas.ExcelAsync(
            handler, AlcancePlataforma with { Organismos = consulta.Organismos }, consulta.Solicitud!, page, pageSize, ct)
            .ConfigureAwait(false);
    }
}
