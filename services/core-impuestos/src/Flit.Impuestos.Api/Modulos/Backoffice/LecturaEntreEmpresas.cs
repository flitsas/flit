using Flit.Impuestos.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Backoffice;

/// <summary>
/// Única lectura entre compañías del servicio (plan §5.1): <c>IgnoreQueryFilters()</c> solo aquí, en <c>Modulos/Backoffice</c>
/// (<c>IgnoreQueryFiltersSoloEnBackofficeTests</c>), detrás de <c>PoliticaBackoffice</c> y con la auditoría que registra quien la
/// llama. Solo lectura (<c>AsNoTracking</c>): una escritura sobre otra compañía sigue exigiendo <c>AutorizarEscrituraDe</c>.
/// </summary>
/// <example>
/// <code>
/// var filas = await lectura.AprovisionamientosAsync(ct);                 // todas las compañías con Impuestos
/// var (fila, historial) = await lectura.AprovisionamientoAsync(empresa, ct); // una, con su historial (más reciente primero)
/// var lotes = lectura.LiquidacionesDe(empresas);                           // vista global del dashboard y los reportes (M14)
/// </code>
/// </example>
public sealed class LecturaEntreEmpresas(ImpuestosDb db)
{
    /// <summary>El aprovisionamiento de cada compañía que tiene o tuvo Impuestos (incluidas las suspendidas, F4-CF10).</summary>
    public Task<List<AprovisionamientoEmpresa>> AprovisionamientosAsync(CancellationToken ct) =>
        db.Set<AprovisionamientoEmpresa>().IgnoreQueryFilters().AsNoTracking().ToListAsync(ct);

    /// <summary>El aprovisionamiento de <paramref name="empresa"/> con su historial; null si la compañía no tiene Impuestos.</summary>
    public async Task<(AprovisionamientoEmpresa Fila, List<HistorialAprovisionamiento> Historial)?> AprovisionamientoAsync(Guid empresa, CancellationToken ct)
    {
        var fila = await db.Set<AprovisionamientoEmpresa>().IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(a => a.TenantId == empresa, ct).ConfigureAwait(false);
        if (fila is null)
            return null;
        var historial = await db.Set<HistorialAprovisionamiento>().IgnoreQueryFilters().AsNoTracking()
            .Where(h => h.TenantId == empresa)
            .OrderByDescending(h => h.En).ThenByDescending(h => h.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        return (fila, historial);
    }

    /// <summary>
    /// Vista global del Super Admin (M14, F4-CF5): las cabeceras de <paramref name="empresas"/> (las que ya auditó quien llama, nunca
    /// otras). Consulta componible y sin seguimiento.
    /// </summary>
    public IQueryable<Liquidacion> LiquidacionesDe(IReadOnlyCollection<Guid> empresas)
    {
        var ids = Lista(empresas);
        return db.Set<Liquidacion>().IgnoreQueryFilters().AsNoTracking().Where(l => ids.Contains(l.TenantId));
    }

    /// <summary>Las placas de <paramref name="empresas"/> (vista global, M14).</summary>
    public IQueryable<LiquidacionItem> ItemsDe(IReadOnlyCollection<Guid> empresas)
    {
        var ids = Lista(empresas);
        return db.Set<LiquidacionItem>().IgnoreQueryFilters().AsNoTracking().Where(i => ids.Contains(i.TenantId));
    }

    /// <summary>Las vigencias de <paramref name="empresas"/> (vista global, M14).</summary>
    public IQueryable<LiquidacionPeriodo> PeriodosDe(IReadOnlyCollection<Guid> empresas)
    {
        var ids = Lista(empresas);
        return db.Set<LiquidacionPeriodo>().IgnoreQueryFilters().AsNoTracking().Where(p => ids.Contains(p.TenantId));
    }

    /// <summary>El contexto de la petición (para las tablas globales, como la caché de <c>/fuentes</c>).</summary>
    internal ImpuestosDb Contexto => db;

    private static List<Guid> Lista(IReadOnlyCollection<Guid> empresas)
    {
        ArgumentNullException.ThrowIfNull(empresas);
        return [.. empresas.Where(e => e != Guid.Empty).Distinct()];
    }
}
