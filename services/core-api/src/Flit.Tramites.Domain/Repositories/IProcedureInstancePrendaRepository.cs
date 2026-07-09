using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// Persistencia del agregado de prenda (IT-3). Aislamiento por tenant en el <c>WHERE</c> (mismo patrón que
/// <see cref="IProcedureInstanceRepository"/>); la RLS de la tabla es defensa en profundidad.
/// </summary>
public interface IProcedureInstancePrendaRepository
{
    /// <summary>Decisión de prenda vigente de la instancia, o <c>null</c> si no hay ninguna.</summary>
    Task<ProcedureInstancePrenda?> GetVigenteAsync(Guid procedureInstanceId, Guid tenantId, CancellationToken ct = default);

    /// <summary>Historial completo de decisiones (vigente + reemplazadas), de la más reciente a la más antigua.</summary>
    Task<IReadOnlyList<ProcedureInstancePrenda>> ListByInstanceAsync(Guid procedureInstanceId, Guid tenantId, CancellationToken ct = default);

    Task AddAsync(ProcedureInstancePrenda prenda, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
