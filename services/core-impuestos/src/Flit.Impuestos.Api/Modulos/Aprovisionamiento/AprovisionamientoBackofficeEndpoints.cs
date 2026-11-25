using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Aprovisionamiento;

/// <summary>Una transición del historial (<c>AprovisionamientoDetalle.historial[]</c>); motivo null si no es un código del contrato.</summary>
public sealed record TransicionAprovisionamientoDto(string? Desde, string Hacia, string? Motivo, string ActorTipo, DateTimeOffset En);

/// <summary>
/// <c>AprovisionamientoDetalle</c> de <c>impuestos.v1.yaml</c> 1.1.0 sin <c>empresa</c> (opcional; lo suma M13, delta 7). Sin
/// secretos: solo estado, motivo, acción, marcas e historial.
/// </summary>
public sealed record AprovisionamientoDetalleDto(
    string Estado, string? Motivo, string? Accion, bool FirmaConfirmada, bool ModoRespaldoFirma, DateTimeOffset ActualizadoEn,
    IReadOnlyList<TransicionAprovisionamientoDto> Historial);

/// <summary>
/// Rutas del Super Admin sobre el aprovisionamiento de una compañía (plan §5.1), bajo <see cref="PoliticaBackoffice"/>, con la
/// compañía explícita de la ruta (<see cref="EmpresaExplicita"/>) y <c>Cache-Control: no-store</c>:
/// <list type="bullet">
///   <item><c>GET</c> (<c>impuestos.backoffice.ver</c>, auditado): estado, motivo, acción, firma, modo respaldo y el historial con
///   la transición más reciente primero; 404 <c>EMPRESA_NO_ENCONTRADA</c> si la compañía no tiene aprovisionamiento.</item>
///   <item><c>POST /reintentar</c> (<c>impuestos.backoffice.ver</c>): 202 desde <c>ERROR</c>, <c>INCIERTO</c> o
///   <c>REQUIERE_REVISION_NIT</c> (pasa a <c>PENDIENTE</c> y se evalúa en la siguiente ronda); 409
///   <c>ESTADO_NO_REINTENTABLE</c> en otro estado; 404 sin aprovisionamiento.</item>
/// </list>
/// </summary>
/// <example>
/// <code>
/// AprovisionamientoBackofficeEndpoints.Map(api); // api = app.MapGroup(Program.Prefijo).RequireAuthorization()
/// </code>
/// </example>
internal static class AprovisionamientoBackofficeEndpoints
{
    public const string Ruta = "/backoffice/empresas/{tenantId}/aprovisionamiento";
    public const string Recurso = "aprovisionamiento";
    public const string CodigoNoReintentable = "ESTADO_NO_REINTENTABLE";
    public const string LiteralNoReintentable = "En el estado actual de la compañía no se puede reintentar.";
    public const string CodigoEmpresaNoEncontrada = "EMPRESA_NO_ENCONTRADA";
    public const string LiteralEmpresaNoEncontrada = "La compañía no existe o no tiene Impuestos.";

    public static void Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var grupo = api.MapGroup(Ruta).RequireAuthorization(PoliticaBackoffice.Nombre).SinCacheEnRespuesta();
        grupo.MapGet(string.Empty, VerAsync).RequirePermiso(Permisos.BackofficeVer).ConEmpresaExplicita(Recurso)
            .WithName("VerAprovisionamientoEmpresaImpuestos");
        grupo.MapPost("/reintentar", ReintentarAsync).RequirePermiso(Permisos.BackofficeVer).ConEmpresaExplicita(Recurso)
            .WithName("ReintentarAprovisionamientoEmpresaImpuestos");
    }

    private static async Task<IResult> VerAsync(HttpContext http, AlmacenCredenciales almacen, CancellationToken ct)
    {
        var empresa = EmpresaExplicita.De(http);
        await using var db = almacen.ContextoDe(empresa);
        var fila = await db.Set<AprovisionamientoEmpresa>().AsNoTracking().SingleOrDefaultAsync(a => a.TenantId == empresa, ct).ConfigureAwait(false)
            ?? throw NoEncontrada();
        var historial = await db.Set<HistorialAprovisionamiento>().AsNoTracking()
            .Where(h => h.TenantId == empresa)
            .OrderByDescending(h => h.En).ThenByDescending(h => h.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(new AprovisionamientoDetalleDto(
            CodigosDeEnum.Codigo(fila.Estado),
            MotivosAprovisionamiento.EsDelContrato(fila.MotivoCodigo) ? fila.MotivoCodigo : null,
            MotivosAprovisionamiento.Accion(fila.Estado),
            fila.FirmaConfirmada,
            fila.ModoRespaldoFirma,
            fila.ActualizadoEn,
            [.. historial.Select(h => new TransicionAprovisionamientoDto(
                h.Desde is { } desde ? CodigosDeEnum.Codigo(desde) : null,
                CodigosDeEnum.Codigo(h.Hacia),
                MotivosAprovisionamiento.EsDelContrato(h.MotivoCodigo) ? h.MotivoCodigo : null,
                h.ActorTipo,
                h.En))]));
    }

    private static async Task<IResult> ReintentarAsync(
        HttpContext http, AlmacenCredenciales almacen, UsuarioActual usuario, ReportadorEstado reportador, TimeProvider reloj, CancellationToken ct)
    {
        var empresa = EmpresaExplicita.De(http);
        await using (var db = almacen.ContextoDe(empresa))
        {
            var fila = await db.Set<AprovisionamientoEmpresa>().SingleOrDefaultAsync(a => a.TenantId == empresa, ct).ConfigureAwait(false)
                ?? throw NoEncontrada();
            if (!ConsumidorReintentoAprovisionamiento.Reintentar(db, fila, usuario.Id, reloj.GetUtcNow()))
                throw ReglaException.Conflicto(CodigoNoReintentable, LiteralNoReintentable);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        // Sin esperar la ronda: el admin de compañías ve PENDIENTE de una vez (si Identidad no responde, la ronda lo reintenta).
        await reportador.ReportarAsync(empresa, ct).ConfigureAwait(false);
        return Results.Accepted();
    }

    private static ReglaException NoEncontrada() =>
        new(CodigoEmpresaNoEncontrada, LiteralEmpresaNoEncontrada, StatusCodes.Status404NotFound);
}
