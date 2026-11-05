using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Backoffice;

/// <summary>
/// Única vía del servicio para escribir en las filas de otra empresa (HU #13741, condición de ADR-0073 para la escritura del
/// backoffice). La empresa sale de <c>EmpresaExplicita.De(http)</c> en el grupo <c>/backoffice</c>, nunca del cuerpo ni del
/// token. <see cref="ParaEditarDeLaEmpresa{T}"/> lee con seguimiento las filas de UNA empresa; <see cref="GuardarDeLaEmpresaAsync"/>
/// guarda dentro del ámbito <c>DiagnosticoDb.AutorizarEscrituraDe</c>, que solo acepta filas nuevas o modificadas de esa
/// empresa y de los tipos indicados (nunca un borrado). Fuera del ámbito, <c>TenantFilter.ValidarEscritura</c> sigue
/// rechazando toda fila ajena. Es <c>internal</c> al módulo Backoffice y <c>EscrituraEntreEmpresasSoloEnBackofficeTests</c>
/// falla si el ámbito se abre fuera de <c>Modulos/Backoffice/</c>.
/// </summary>
internal static class EscrituraEntreEmpresas
{
    /// <summary>Filas de <paramref name="empresa"/> con seguimiento de cambios, para editarlas.</summary>
    public static IQueryable<T> ParaEditarDeLaEmpresa<T>(this DiagnosticoDb db, Guid empresa)
        where T : class, IEntidadDeEmpresa
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Set<T>().IgnoreQueryFilters().Where(e => e.TenantId == empresa);
    }

    /// <summary>Guarda los cambios pendientes con la escritura abierta solo para <paramref name="empresa"/> y <paramref name="tipos"/>.</summary>
    public static async Task GuardarDeLaEmpresaAsync(this DiagnosticoDb db, Guid empresa, Type[] tipos, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        using (db.AutorizarEscrituraDe(empresa, tipos))
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
