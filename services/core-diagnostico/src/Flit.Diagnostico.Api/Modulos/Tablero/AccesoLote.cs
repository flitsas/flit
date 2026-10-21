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
        ArgumentNullException.ThrowIfNull(usuario);
        usuario.EmpresaPropia();
        var consulta = db.Set<CabeceraDiagnostico>().Where(c => c.Id == id);
        var cabecera = await (seguir ? consulta : consulta.AsNoTracking()).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (cabecera is null || !await PuedeVerAsync(db, usuario, cabecera, ct).ConfigureAwait(false))
            throw ReglaException.NoEncontrado("El diagnóstico");
        return cabecera;
    }

    /// <summary>El admin de la compañía ve todos los lotes de su empresa sin importar la visibilidad (§6.2).</summary>
    public static bool EsAdminDeLaCompania(UsuarioActual usuario)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        return usuario.Tiene(Permisos.ConfiguracionEditar);
    }

    private static async Task<bool> PuedeVerAsync(DiagnosticoDb db, UsuarioActual usuario, CabeceraDiagnostico cabecera, CancellationToken ct)
    {
        if (cabecera.UsuarioCreadorId == usuario.Id || EsAdminDeLaCompania(usuario))
            return true;
        var visibilidad = await db.Set<ConfiguracionEmpresa>().AsNoTracking()
            .Select(c => (VisibilidadUsuario?)c.VisibilidadUsuario)
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        return (visibilidad ?? VisibilidadUsuario.Empresa) == VisibilidadUsuario.Empresa;
    }
}
