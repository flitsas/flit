using Flit.Admin.Application.Consolidados;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Services;

/// <summary>
/// HU #12789 (Épica #12760) — implementación de <see cref="IConsolidadoInvalidacionMasiva"/>.
/// <para>
/// Cada método emite UN solo <c>UPDATE ... WHERE ...</c> (<c>ExecuteUpdateAsync</c>, AC5): un OT con
/// cientos de trámites en curso se invalida sin materializar entidades ni recorrerlas en bucle, y el
/// filtro por OT se resuelve como subconsulta dentro del mismo comando. LINQ parametrizado, sin SQL
/// interpolado (<c>SqlInterpolationArchitectureTests</c>).
/// </para>
/// <para>
/// El predicado se expone como <c>internal</c> (<see cref="CandidatasPorOt"/>,
/// <see cref="CandidatasPorTenant"/>) para verificar QUÉ filas alcanza con el proveedor en memoria,
/// que no soporta <c>ExecuteUpdate</c>.
/// </para>
/// <para>
/// El UPDATE va por fuera del ChangeTracker: no pasa por <c>ConsolidadoVigenciaTracker</c> (que solo
/// mira hijos del expediente) y no toca <c>updated_at</c> — invalidar la caché no es una edición del
/// trámite.
/// </para>
/// </summary>
internal sealed class ConsolidadoInvalidacionMasiva : IConsolidadoInvalidacionMasiva
{
    private readonly FlitDbContext _context;

    public ConsolidadoInvalidacionMasiva(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<int> InvalidarPorPrelacionOtAsync(
        Guid otTenantId,
        Guid procedureTypeId,
        CancellationToken cancellationToken = default)
    {
        if (otTenantId == Guid.Empty || procedureTypeId == Guid.Empty)
        {
            return Task.FromResult(0);
        }

        return InvalidarAsync(CandidatasPorOt(_context, otTenantId, procedureTypeId), cancellationToken);
    }

    public Task<int> InvalidarPorCompaniaAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            return Task.FromResult(0);
        }

        return InvalidarAsync(CandidatasPorTenant(_context, tenantId), cancellationToken);
    }

    // AC3 — sin relación persistida firma↔trámite (la firma se resuelve por tenant + documento del
    // firmante en cada generación), la unidad fiable es la compañía titular del baúl.
    public Task<int> InvalidarPorFirmaBaulAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        InvalidarPorCompaniaAsync(tenantId, cancellationToken);

    /// <summary>
    /// Trámites NO finales, no borrados, con al menos un consolidado vigente (los ya invalidados no se
    /// reescriben: menos filas tocadas y menos bumps de <c>row_version</c>).
    /// </summary>
    internal static IQueryable<ProcedureInstance> Vigentes(FlitDbContext context)
    {
        // Colección de estados finales del dominio (RF04): aprobado, anulado, revocado (AC4).
        string[] finales = [.. TramiteEstado.Finales];

        return context.ProcedureInstances
            .Where(i => i.DeletedAt == null
                && !finales.Contains(i.Status)
                && (i.ConsolidadoWizardVigente || i.ConsolidadoMaestroVigente));
    }

    /// <summary>
    /// AC1 — trámites radicados ante el OT del tenant <paramref name="otTenantId"/>, del tipo cuya
    /// prelación cambió. El trámite vive en el tenant del cliente y la prelación en el del organismo:
    /// el puente es <c>admin.transit_office_profiles</c>, igual que en
    /// <see cref="OtConfiguredDocumentOrderProvider"/>.
    /// </summary>
    internal static IQueryable<ProcedureInstance> CandidatasPorOt(
        FlitDbContext context,
        Guid otTenantId,
        Guid procedureTypeId)
    {
        var oficinasDelOt = context.TransitOfficeProfiles
            .Where(p => p.TenantId == otTenantId)
            .Select(p => (Guid?)p.TransitOfficeId);

        return Vigentes(context)
            .Where(i => i.ProcedureTypeId == procedureTypeId
                && i.TransitOfficeId != null
                && oficinasDelOt.Contains(i.TransitOfficeId));
    }

    /// <summary>AC2/AC3 — trámites de la compañía (tenant) dueña del directorio o del baúl.</summary>
    internal static IQueryable<ProcedureInstance> CandidatasPorTenant(FlitDbContext context, Guid tenantId) =>
        Vigentes(context).Where(i => i.TenantId == tenantId);

    private static Task<int> InvalidarAsync(
        IQueryable<ProcedureInstance> candidatas,
        CancellationToken cancellationToken) =>
        candidatas.ExecuteUpdateAsync(
            s => s
                .SetProperty(i => i.ConsolidadoWizardVigente, false)
                .SetProperty(i => i.ConsolidadoMaestroVigente, false),
            cancellationToken);
}
