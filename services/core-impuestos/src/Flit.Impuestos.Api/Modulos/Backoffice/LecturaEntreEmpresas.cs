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
}
