using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Sesion;

/// <summary>
/// <c>GET /sesion</c> (plan §5.1): lo que la app necesita al abrir — usuario, compañía, roles, permisos de Impuestos,
/// <c>esSuperAdmin</c> y el estado de aprovisionamiento de la compañía. Todo sale del token salvo el aprovisionamiento, que se lee
/// de <c>impuestos.aprovisionamientos</c> con el filtro por empresa (sin fila: <c>estado</c> null). Una sesión = una compañía: no
/// hay parámetro de compañía y cualquiera que llegue se ignora.
/// </summary>
internal static class SesionEndpoints
{
    public const string Ruta = "/sesion";

    public static void Map(RouteGroupBuilder api) =>
        api.MapGet(Ruta, ObtenerAsync).RequireAuthorization(AutorizacionExtensions.PoliticaUsuario).WithName("ObtenerSesionImpuestos");

    private static async Task<IResult> ObtenerAsync(UsuarioActual usuario, ImpuestosDb db, CancellationToken ct)
    {
        var empresa = usuario.EmpresaPropia();
        var aprovisionamiento = await db.Set<AprovisionamientoEmpresa>().AsNoTracking()
            .Where(a => a.TenantId == empresa)
            .Select(a => new { a.Estado, a.FirmaConfirmada })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return Results.Ok(new
        {
            usuario = new { id = usuario.Id, email = usuario.Email },
            empresa = new { id = empresa, nombre = usuario.EmpresaNombre },
            roles = usuario.Roles,
            permisos = usuario.PermisosDelProducto,
            esSuperAdmin = usuario.EsSuperAdmin,
            aprovisionamiento = new
            {
                estado = aprovisionamiento is null ? null : CodigosDeEnum.Codigo(aprovisionamiento.Estado),
                firmaConfirmada = aprovisionamiento?.FirmaConfirmada ?? false,
            },
        });
    }
}
