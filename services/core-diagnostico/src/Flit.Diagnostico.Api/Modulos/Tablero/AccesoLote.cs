using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Configuracion;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Tablero;

/// <summary>
/// Quién ve un lote del tablero (HU #13743, F04-C12, D-01, R-21, plan §6.2): siempre la compañía del token (el filtro de
/// <c>TenantFilter</c> deja fuera la de otra compañía, que responde 404 sin datos). Dentro de la compañía, con la
/// <c>visibilidad_usuario</c> vigente de su configuración (sin fila, <c>EMPRESA</c>): con <c>PROPIOS</c> el usuario solo ve los
/// lotes que creó y el admin de la compañía (el que tiene <c>diagnostico.configuracion.editar</c>, exclusivo de
/// <c>admin_diagnostico</c>, o el SuperAdmin) ve todos. Un lote que no puede ver responde igual que uno que no existe (404).
/// </summary>
internal static class AccesoLote
{
    /// <summary>La cabecera si el usuario la puede ver; 404 <c>NO_ENCONTRADO</c> si no existe, es de otra compañía o no es suya.</summary>
    /// <param name="seguir">true si el llamador la va a modificar (la deja seguida por el contexto).</param>
    public static async Task<CabeceraDiagnostico> LeerVisibleAsync(DiagnosticoDb db, UsuarioActual usuario, Guid id, bool seguir, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var consulta = (await VisiblesAsync(db.Set<CabeceraDiagnostico>(), db, usuario, ct).ConfigureAwait(false)).Where(c => c.Id == id);
        return await (seguir ? consulta : consulta.AsNoTracking()).SingleOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw ReglaException.NoEncontrado("El diagnóstico");
    }

    /// <summary>
    /// Los lotes de <paramref name="cabeceras"/> que el usuario puede ver (HU #13845): la MISMA regla para el listado de
    /// <c>GET /diagnosticos</c> y para la lectura individual de <see cref="LeerVisibleAsync"/>, que la usa. La compañía la pone
    /// el filtro global de <c>TenantFilter</c>; el admin de la compañía ve todos; con <c>PROPIOS</c> los demás solo los que
    /// crearon; sin fila de configuración, <c>EMPRESA</c>. 403 <c>SIN_EMPRESA</c> si el token no trae compañía.
    /// </summary>
    public static async Task<IQueryable<CabeceraDiagnostico>> VisiblesAsync(
        IQueryable<CabeceraDiagnostico> cabeceras, DiagnosticoDb db, UsuarioActual usuario, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cabeceras);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(usuario);
        usuario.EmpresaPropia();
        if (EsAdminDeLaCompania(usuario))
            return cabeceras;
        var visibilidad = await db.Set<ConfiguracionEmpresa>().AsNoTracking()
            .Select(c => (VisibilidadUsuario?)c.VisibilidadUsuario)
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if ((visibilidad ?? VisibilidadUsuario.Empresa) == VisibilidadUsuario.Empresa)
            return cabeceras;
        var propio = usuario.Id;
        return cabeceras.Where(c => c.UsuarioCreadorId == propio);
    }

    /// <summary>El admin de la compañía ve todos los lotes de su empresa sin importar la visibilidad (§6.2).</summary>
    public static bool EsAdminDeLaCompania(UsuarioActual usuario)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        return usuario.Tiene(Permisos.ConfiguracionEditar);
    }
}
