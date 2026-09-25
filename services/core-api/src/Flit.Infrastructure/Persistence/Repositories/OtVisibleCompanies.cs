using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Bug #12912 (Ley 1581, decisión del usuario) — ÚNICA definición de qué compañías puede ver por
/// nombre un organismo de tránsito (usuario no SuperAdmin). La usan el alcance de consultas y métricas
/// (<see cref="OtTenantScope"/>), la consola de mandatarios (<see cref="DbMandateSignerReader"/>) y la
/// configuración de mandatos del hub OT (<c>MandateConfigAdminService</c>): tenerla escrita una vez
/// evita que las copias se desincronicen y una de ellas filtre nombres de otra red.
/// <para>Visibles = (grant directo habilitado ∩ lista efectiva del OT) ∪ (compañías que entran solo
/// por la red y ya le entregaron algún trámite). «Entregado» usa el universo de la bandeja:
/// <see cref="TramiteEstado.RecibidosPorOrganismo"/>, sin borrados.</para>
/// <para>Debe ejecutarse dentro de la lectura cross-tenant del llamador (<c>row_security = off</c>).</para>
/// </summary>
internal static class OtVisibleCompanies
{
    /// <param name="context">Contexto EF del llamador (dentro de su lectura cross-tenant).</param>
    /// <param name="transitOfficeId">Organismo.</param>
    /// <param name="effectiveTenantIds">Compañías que pueden radicar en el organismo (lista efectiva inversa).</param>
    /// <param name="cancellationToken">Cancelación.</param>
    public static async Task<IReadOnlyList<Guid>> FilterAsync(
        FlitDbContext context,
        Guid transitOfficeId,
        IReadOnlyCollection<Guid> effectiveTenantIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(effectiveTenantIds);

        if (effectiveTenantIds.Count == 0)
        {
            return [];
        }

        var directos = await context.TenantTransitOfficeGrants
            .AsNoTracking()
            .Where(g => g.TransitOfficeId == transitOfficeId && g.IsEnabled)
            .Select(g => g.TenantId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Un grant propio que la red no respalda (hija de Concesión cuya cabeza no tiene el OT) no
        // habilita a radicar, así que tampoco hace visible a la compañía (review PR #442, obs. 3).
        var directosRespaldados = directos.Intersect(effectiveTenantIds).ToList();

        var soloRed = effectiveTenantIds.Except(directos).ToList();
        var redConTramites = soloRed.Count == 0
            ? []
            : await context.ProcedureInstances
                .AsNoTracking()
                .Where(p => p.DeletedAt == null
                    && p.TransitOfficeId == transitOfficeId
                    && TramiteEstado.RecibidosPorOrganismo.Contains(p.Status)
                    && soloRed.Contains(p.TenantId))
                .Select(p => p.TenantId)
                .Distinct()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        return [.. directosRespaldados.Union(redConTramites)];
    }
}
