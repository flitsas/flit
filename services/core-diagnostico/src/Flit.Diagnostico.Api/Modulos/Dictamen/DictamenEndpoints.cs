using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;

namespace Flit.Diagnostico.Api.Modulos.Dictamen;

/// <summary>
/// <c>GET /diagnosticos/{id}/vehiculos/{vid}/dictamen</c> (corte 7). Permiso <c>diagnostico.diagnosticos.ver</c> y la
/// visibilidad de <see cref="Tablero.AccesoLote"/>. No hay <c>PUT</c> en esta ruta: el reemplazo vive en el backoffice y un
/// <c>PUT</c> aquí responde 404.
/// </summary>
internal static class DictamenEndpoints
{
    public const string Ruta = DiagnosticosEndpoints.Ruta + "/{id:guid}/vehiculos/{vid:guid}/dictamen";

    public static void Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        api.MapGet(Ruta, Obtener).RequirePermiso(Permisos.DiagnosticosVer).WithName("ObtenerDictamenVehiculo");
    }

    private static async Task<IResult> Obtener(Guid id, Guid vid, DiagnosticoDb db, UsuarioActual usuario, CancellationToken ct) =>
        Results.Ok(await ConsultaDictamen.LeerDeLaCompaniaAsync(db, usuario, id, vid, ct).ConfigureAwait(false));
}
