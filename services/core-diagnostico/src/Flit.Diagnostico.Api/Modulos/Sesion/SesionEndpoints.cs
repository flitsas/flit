using Flit.Diagnostico.Api.Plataforma;

namespace Flit.Diagnostico.Api.Modulos.Sesion;

/// <summary>
/// <c>GET /sesion</c> (HU #13724, plan §6.1): lo que la app necesita al abrir — usuario, compañía, roles, permisos de
/// Diagnóstico y capacidades. Todo sale del token; no lee la base. Una sesión = una compañía (D-03): no hay parámetro
/// de compañía y cualquiera que llegue se ignora.
/// </summary>
internal static class SesionEndpoints
{
    public const string Ruta = "/sesion";

    public static void Map(RouteGroupBuilder api) =>
        api.MapGet(Ruta, Obtener).RequireAuthorization(AutorizacionExtensions.PoliticaUsuario).WithName("ObtenerSesionDiagnostico");

    private static IResult Obtener(UsuarioActual usuario)
    {
        var empresa = usuario.EmpresaPropia();
        return Results.Ok(new
        {
            usuario = new { id = usuario.Id, email = usuario.Email, esSuperAdmin = usuario.EsSuperAdmin },
            empresa = new { id = empresa, nombre = usuario.EmpresaNombre, tipo = usuario.TipoEmpresa },
            roles = usuario.Roles,
            permisos = usuario.PermisosDelProducto,
            capacidades = Capacidades.De(usuario),
        });
    }
}
