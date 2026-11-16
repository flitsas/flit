using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Backoffice;

/// <summary>
/// Único punto del servicio que se salta el filtro global por empresa (HU #13728, ADR-0072 punto 3, ADR-0073): lee las filas
/// de UNA empresa, la indicada en la ruta del backoffice, sin seguimiento de cambios. Es <c>internal</c> al módulo
/// Backoffice y <c>IgnoreQueryFiltersSoloEnBackofficeTests</c> falla si <c>IgnoreQueryFilters()</c> aparece fuera de
/// <c>Modulos/Backoffice/</c>.
/// </summary>
internal static class LecturaEntreEmpresas
{
    public static IQueryable<T> DeLaEmpresa<T>(this DiagnosticoDb db, Guid empresa)
        where T : class, IEntidadDeEmpresa
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Set<T>().IgnoreQueryFilters().AsNoTracking().Where(e => e.TenantId == empresa);
    }
}
