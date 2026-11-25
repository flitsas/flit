using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Aprovisionamiento;

/// <summary>
/// «Reintentar» del Super Admin desde Impuestos (plan §5.1): <c>POST /backoffice/empresas/{tenantId}/aprovisionamiento/reintentar</c>
/// (<c>impuestos.backoffice.ver</c>), bajo <see cref="PoliticaBackoffice"/>, con la compañía explícita de la ruta
/// (<see cref="EmpresaExplicita"/>) y <c>Cache-Control: no-store</c>: 202 desde <c>ERROR</c>, <c>INCIERTO</c>,
/// <c>REQUIERE_REVISION_NIT</c> o <c>LISTO_SIN_WEBHOOK</c> (pasa a <c>PENDIENTE</c> y se evalúa en la siguiente ronda); 409 <c>ESTADO_NO_REINTENTABLE</c> en
/// otro estado; 404 <c>EMPRESA_NO_ENCONTRADA</c> sin aprovisionamiento. El <c>GET</c> del estado con su historial es de M13
/// (<c>EmpresasBackofficeEndpoints.DetalleAsync</c>).
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
        grupo.MapPost("/reintentar", ReintentarAsync).RequirePermiso(Permisos.BackofficeVer).ConEmpresaExplicita(Recurso)
            .WithName("ReintentarAprovisionamientoEmpresaImpuestos");
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
