using System.Globalization;
using Flit.Api.Authorization;
using Flit.Api.PersonaNatural;
using Flit.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Flit.Api.Endpoints;

/// <summary>
/// HU #14461 (Feature #14453, Épica #14210, ADR-0082) — «Cobros de caja» del organismo desde el hub del OT: core-api es la fachada y
/// el cobro vive en el portal (<c>CajaAdminService</c> por gRPC con <c>svc-tramites</c>). Solo el administrador del OT y el SuperAdmin
/// (<see cref="AdminAuthorization.OtAdminOrSuperAdminPolicy"/>): la caja NO anula. El organismo sale del token del administrador (ajeno
/// da 403) o de <c>?transitOfficeId=</c> si es SuperAdmin; el portal no lo discute. Errores <c>{ code, message }</c>. 404 con
/// <c>PortalCaja:Habilitado</c> apagado.
/// </summary>
public static class AdminPortalCajaCobrosEndpoints
{
    public const string BasePath = "/api/v1/admin/ot/portal-caja/cobros";

    public const int MotivoMinimo = 10;
    public const int MotivoMaximo = 300;
    public const int TamanoMaximo = 100;

    public sealed record AnularCobroCajaRequest(string? Reason, long? RowVersion);

    public static IEndpointRouteBuilder MapAdminPortalCajaCobrosEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var group = app.MapGroup(BasePath)
            .RequireAuthorization(AdminAuthorization.OtAdminOrSuperAdminPolicy)
            .AddEndpointFilter(PortalCajaHabilitadoFilter.Instance)
            .WithTags("Admin · OT · Caja del portal");

        group.MapGet("", ListarAsync)
            .WithName("AdminOtPortalCajaCobrosList")
            .WithSummary("Cobros en caja del organismo en un día de Colombia (por defecto hoy), con el total del día sin los anulados")
            .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/{cobroId:guid}/anular", AnularAsync)
            .WithName("AdminOtPortalCajaCobrosAnular")
            .WithSummary("Anula un cobro en caja con motivo obligatorio; solo mientras el trámite no haya llegado a FLIT 2.0")
            .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict).Produces(StatusCodes.Status503ServiceUnavailable);
        return app;
    }

    private static async Task<IResult> ListarAsync(
        HttpContext http, FlitDbContext db, CancellationToken ct,
        [FromQuery] Guid? transitOfficeId = null, [FromQuery] string? date = null, [FromQuery] int? page = null, [FromQuery] int? pageSize = null)
    {
        var (alcance, error) = await AlcancePortalCaja.ResolverAsync(http.User, transitOfficeId, db, ct).ConfigureAwait(false);
        if (error is not null)
            return error;

        string? fecha = null;
        if (!string.IsNullOrWhiteSpace(date))
        {
            if (!DateOnly.TryParseExact(date.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia))
                return AlcancePortalCaja.Error(StatusCodes.Status400BadRequest, "INVALID_DATE", "La fecha debe ser AAAA-MM-DD.");
            fecha = dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        var pagina = Math.Max(1, page ?? 1);
        var tamano = Math.Clamp(pageSize ?? 20, 1, TamanoMaximo);
        var portal = http.RequestServices.GetRequiredService<ICajaAdminClient>(); // solo existe con PortalCaja:Habilitado
        try
        {
            var r = await portal.ListarAsync(alcance!.TransitOfficeId, fecha, pagina, tamano, ct).ConfigureAwait(false);
            return Results.Ok(new
            {
                items = r.Items.Select(c => new
                {
                    cobroId = c.CobroId,
                    codigo = c.Codigo,
                    valorCop = c.ValorCop,
                    medio = c.Medio,
                    medioDetalle = c.MedioDetalle,
                    pagadoEn = c.PagadoEn,
                    pagadoPorNombre = c.PagadoPorNombre,
                    estado = c.Estado,
                    anulable = c.Anulable,
                    motivoNoAnulable = c.MotivoNoAnulable,
                    anuladoEn = c.AnuladoEn,
                    anuladoPorNombre = c.AnuladoPorNombre,
                    anulacionMotivo = c.AnulacionMotivo,
                    rowVersion = c.RowVersion,
                    anulableHasta = c.Anulable && c.AnulableHasta.Length > 0 ? c.AnulableHasta : null,
                }),
                total = r.Total,
                totalDiaCop = r.TotalDiaCop,
            });
        }
        catch (CajaPortalNoDisponibleException)
        {
            return PortalNoDisponible();
        }
    }

    private static async Task<IResult> AnularAsync(
        Guid cobroId, AnularCobroCajaRequest? body, HttpContext http, FlitDbContext db, CancellationToken ct,
        [FromQuery] Guid? transitOfficeId = null)
    {
        var (alcance, error) = await AlcancePortalCaja.ResolverAsync(http.User, transitOfficeId, db, ct).ConfigureAwait(false);
        if (error is not null)
            return error;
        if (AdminCompaniesDomainEndpoints.ResolveUserId(http.User) is not { } actor)
            return AlcancePortalCaja.Error(StatusCodes.Status401Unauthorized, "USUARIO_NO_IDENTIFICADO", "No se pudo identificar al usuario.");

        var motivo = body?.Reason?.Trim();
        if (string.IsNullOrEmpty(motivo))
            return AlcancePortalCaja.Error(StatusCodes.Status400BadRequest, "REASON_REQUIRED", "Escriba el motivo de la anulación.");
        if (motivo.Length is < MotivoMinimo or > MotivoMaximo)
            return AlcancePortalCaja.Error(
                StatusCodes.Status400BadRequest, "REASON_LENGTH", $"El motivo debe tener entre {MotivoMinimo} y {MotivoMaximo} caracteres.");

        // Sin la versión del cobro no hay control de concurrencia: se exige (viene de la lista) en vez de tratar 0 como «cualquiera».
        if (body?.RowVersion is not > 0)
            return AlcancePortalCaja.Error(StatusCodes.Status400BadRequest, "ROW_VERSION_REQUIRED", "Falta la versión del cobro. Actualice la lista e intente de nuevo.");

        var portal = http.RequestServices.GetRequiredService<ICajaAdminClient>(); // solo existe con PortalCaja:Habilitado
        var nombre = await db.Users.AsNoTracking().Where(u => u.Id == actor).Select(u => u.DisplayName)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? string.Empty;
        try
        {
            var r = await portal.AnularAsync(alcance!.TransitOfficeId, cobroId, motivo, actor, nombre, body.RowVersion.Value, ct).ConfigureAwait(false);
            return r.Razon switch
            {
                null => Results.Ok(new { cobroId, estado = r.Estado }),
                CajaAnulacionResultado.NoAnulable => AlcancePortalCaja.Error(
                    StatusCodes.Status409Conflict, "COBRO_NO_ANULABLE", "Este cobro ya no se puede anular: el trámite llegó a FLIT 2.0 o pasó la ventana de anulación."),
                _ => AlcancePortalCaja.Error(StatusCodes.Status404NotFound, "COBRO_NO_ENCONTRADO", "No encontramos ese cobro en este organismo."),
            };
        }
        catch (CajaPortalNoDisponibleException)
        {
            return PortalNoDisponible();
        }
    }

    private static IResult PortalNoDisponible() =>
        AlcancePortalCaja.Error(StatusCodes.Status503ServiceUnavailable, "PORTAL_NO_DISPONIBLE", "El portal de persona natural no responde. Intente de nuevo en unos minutos.");
}
