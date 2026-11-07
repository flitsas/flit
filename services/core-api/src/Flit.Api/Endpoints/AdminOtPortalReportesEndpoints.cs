using System.Security.Claims;
using Flit.Admin.Application.Plataforma.PersonaNatural;
using Flit.Admin.Domain.Plataforma.PersonaNatural;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// HU #14397 y #14398 (Feature #14396, Épica #14297) — reporte de los trámites del portal Persona Natural que cayeron a FLIT 2.0,
/// visto desde el dashboard del organismo: <c>/api/v1/admin/ot/portal-reportes</c> (+ <c>/export</c>). El administrador del
/// organismo ve web y punto de SU organismo (el del token); un <c>transitOfficeId</c> ajeno da 403 y no se lee nada. El SuperAdmin
/// envía <c>?transitOfficeId=</c> (obligatorio) y ve ese organismo con la misma vista. El gestor nunca aparece. Errores:
/// <c>{ code, message }</c>. La vista de Plataforma vive en <see cref="AdminPlataformaPersonaNaturalEndpoints"/>.
/// </summary>
public static class AdminOtPortalReportesEndpoints
{
    public const string BasePath = "/api/v1/admin/ot/portal-reportes";

    internal const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static IEndpointRouteBuilder MapAdminOtPortalReportesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup(BasePath)
            .RequireAuthorization(AdminAuthorization.OtAdminOrSuperAdminPolicy)
            .WithTags("Admin · OT · Reportes del portal");

        group.MapGet("", ListarAsync)
            .WithName("AdminOtPortalReportesList")
            .WithSummary("Trámites del portal que cayeron a FLIT 2.0 en el organismo (web y punto), con filtros y paginación")
            .Produces<PaginaReportePortal>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/export", ExportarAsync)
            .WithName("AdminOtPortalReportesExport")
            .WithSummary("Excel del mismo conjunto filtrado del reporte del organismo")
            .Produces(StatusCodes.Status200OK, contentType: ExcelContentType)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> ListarAsync(
        HttpContext http,
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

        var r = await handler.HandleAsync(Alcance(http.User, consulta.Organismos), consulta.Solicitud!, page, pageSize, ct).ConfigureAwait(false);
        return r.Error is null ? Results.Ok(r.Pagina) : ReportePortalRespuestas.Falla(r.Error);
    }

    private static async Task<IResult> ExportarAsync(
        HttpContext http,
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

        return await ReportePortalRespuestas.ExcelAsync(handler, Alcance(http.User, consulta.Organismos), consulta.Solicitud!, page, pageSize, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// SuperAdmin: el organismo que indica (si falta, o son varios, el alcance lo rechaza con «organismo requerido»). Otro rol: el
    /// tenant del token; lo que el cliente envíe en <c>transitOfficeId</c> solo se compara, nunca decide quién es.
    /// </summary>
    private static AlcanceReportePortal Alcance(ClaimsPrincipal user, IReadOnlyList<Guid> organismos)
    {
        var esSuperAdmin = user.IsInRole(AdminAuthorization.SuperAdminRole)
            || string.Equals(user.FindFirstValue(AdminAuthorization.RoleClaimType), AdminAuthorization.SuperAdminRole, StringComparison.OrdinalIgnoreCase);
        if (esSuperAdmin)
            return new AlcanceReportePortal(Plataforma: false, OtTenantId: null, Organismos: organismos);

        // Sin tenant en el token: Guid.Empty no resuelve organismo y el repositorio responde «sin organismo» (403).
        var tenant = RequestTenantResolver.TryResolveTenantId(user, out var t) ? t : Guid.Empty;
        return new AlcanceReportePortal(Plataforma: false, OtTenantId: tenant, Organismos: organismos);
    }
}

/// <summary>Consulta ya leída de la URL: solicitud, organismos pedidos, o la respuesta 400 si algún valor no se entiende.</summary>
internal sealed record ConsultaReportePortal(SolicitudReportePortal? Solicitud, IReadOnlyList<Guid> Organismos, IResult? Falla);

/// <summary>Respuestas comunes del reporte del portal para sus dos puertas (organismo y Plataforma).</summary>
internal static class ReportePortalRespuestas
{
    /// <summary>
    /// Lee <c>canal</c>, <c>tipoTramiteId</c> y <c>transitOfficeId</c>: cada uno puede ser una lista separada por comas. Un valor
    /// que no es un identificador responde 400 (nunca se descarta en silencio: un filtro ignorado ampliaría el resultado).
    /// </summary>
    public static ConsultaReportePortal Leer(string? canal, string? tipos, string? organismos, DateOnly? desde, DateOnly? hasta)
    {
        if (!TryGuids(tipos, out var idsTipo) || !TryGuids(organismos, out var idsOrganismo))
        {
            return new ConsultaReportePortal(
                null, [], Error(StatusCodes.Status400BadRequest, ReportePortalErrores.FiltroInvalido, "Los identificadores de tipo y de organismo deben ser GUID, separados por coma."));
        }

        return new ConsultaReportePortal(new SolicitudReportePortal(Partir(canal), idsTipo, desde, hasta), idsOrganismo, null);
    }

    private static string[] Partir(string? lista) =>
        string.IsNullOrWhiteSpace(lista) ? [] : lista.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool TryGuids(string? lista, out IReadOnlyList<Guid> ids)
    {
        var salida = new List<Guid>();
        ids = salida;
        foreach (var texto in Partir(lista))
        {
            if (!Guid.TryParse(texto, out var id))
                return false;
            salida.Add(id);
        }

        return true;
    }

    /// <summary>
    /// Genera el libro en un temporal ANTES de responder: un error de alcance sale como JSON y no como un archivo a medias. El
    /// temporal se borra al cerrar la respuesta.
    /// </summary>
    public static async Task<IResult> ExcelAsync(
        ExportarReportePortalHandler handler, AlcanceReportePortal alcance, SolicitudReportePortal solicitud, int? page, int? pageSize, CancellationToken ct)
    {
        var archivo = new FileStream(
            Path.Combine(Path.GetTempPath(), $"flit-reporte-portal-{Guid.NewGuid():N}.xlsx"),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        try
        {
            var error = await handler.HandleAsync(archivo, alcance, solicitud, page, pageSize, ct).ConfigureAwait(false);
            if (error is not null)
            {
                await archivo.DisposeAsync().ConfigureAwait(false);
                return Falla(error);
            }

            archivo.Position = 0;
        }
        catch
        {
            await archivo.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return Results.File(archivo, AdminOtPortalReportesEndpoints.ExcelContentType,
            fileDownloadName: $"reporte_portal_{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    public static IResult Falla(string codigo) => codigo switch
    {
        ReportePortalErrores.OtroOrganismo => Error(StatusCodes.Status403Forbidden, codigo, "No puede consultar los trámites de otro organismo."),
        ReportePortalErrores.SinOrganismo => Error(StatusCodes.Status403Forbidden, codigo, "Su usuario no tiene un organismo de tránsito asociado."),
        ReportePortalErrores.OrganismoRequerido => Error(StatusCodes.Status400BadRequest, codigo, "Indique el organismo (transitOfficeId)."),
        ReportePortalErrores.CanalInvalido => Error(
            StatusCodes.Status400BadRequest, codigo, $"El canal no es válido. Use {string.Join(", ", CanalesReportePortal.Plataforma)}."),
        ReportePortalErrores.RangoInvalido => Error(StatusCodes.Status400BadRequest, codigo, "La fecha inicial no puede ser posterior a la final."),
        ReportePortalErrores.FiltroInvalido => Error(StatusCodes.Status400BadRequest, codigo, "Algún filtro no es válido."),
        ReportePortalErrores.PaginaInvalida => Error(StatusCodes.Status400BadRequest, codigo, "La página debe ser 1 o más y el tamaño, entre 1 y 100."),
        _ => Error(StatusCodes.Status400BadRequest, codigo, "No se pudo consultar el reporte."),
    };

    private static IResult Error(int status, string code, string message) => Results.Json(new { code, message }, statusCode: status);
}
