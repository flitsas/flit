using Flit.Impuestos.Api.Modulos.Configuracion;
using Flit.Impuestos.Api.Plataforma;

namespace Flit.Impuestos.Api.Modulos.Catalogos;

/// <summary>
/// <c>GET /fuentes</c> (plan §5.1, permiso <c>impuestos.configuracion.ver</c>): los departamentos del proveedor para la compañía
/// del token, <c>{ items: [{ codigo, nombre, activa }] }</c> ordenados por código, desde la caché de
/// <see cref="SincronizacionFuentes"/>. 409 <c>APROVISIONAMIENTO_NO_LISTO</c> si la compañía no está en Listo o Listo sin
/// webhook; 503 <c>PROVEEDOR_NO_DISPONIBLE</c> si no hay caché vigente y el proveedor no responde.
/// </summary>
/// <example>
/// <code>
/// FuentesEndpoints.Map(api); // api = app.MapGroup(Program.Prefijo).RequireAuthorization()
/// </code>
/// </example>
internal static class FuentesEndpoints
{
    public const string Ruta = "/fuentes";

    public static void Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        api.MapGet(Ruta, ListarAsync).RequirePermiso(Permisos.ConfiguracionVer).WithName("ListarFuentesImpuestos");
    }

    private static async Task<IResult> ListarAsync(SincronizacionFuentes fuentes, UsuarioActual usuario, CancellationToken ct)
    {
        var lista = await fuentes.ObtenerAsync(usuario.EmpresaPropia(), ct).ConfigureAwait(false);
        return Results.Ok(new FuentesDto([.. lista.Select(f => new DepartamentoDto(f.DepartamentoCodigo, f.Nombre, f.Activa))]));
    }
}

/// <summary>Respuesta de <c>GET /fuentes</c>.</summary>
internal sealed record FuentesDto(IReadOnlyList<DepartamentoDto> Items);
