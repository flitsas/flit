using Flit.Admin.Domain.Common;

namespace Flit.Admin.Domain.Improntas;

/// <summary>
/// Persistencia del historial de improntas generadas (HU #10466 / ADR-0022) y su listado
/// paginado/filtrable (placa, radicado, fecha — HU #10468).
/// </summary>
public interface IImprontaRepository
{
    /// <summary>
    /// Persiste una nueva generación de impronta (metadata + PDF) en una sola operación atómica.
    /// </summary>
    Task SaveAsync(ImprontaGeneration generation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lista paginada del historial de improntas (metadata únicamente, nunca el PDF — ver
    /// <see cref="ImprontaGenerationListItem"/>), ordenada por fecha de creación descendente.
    /// Vista global cross-tenant (SuperAdmin, sin RLS — ADR-0022): no se filtra por tenant.
    /// </summary>
    Task<PagedResult<ImprontaGenerationListItem>> ListAsync(
        ImprontaGenerationFilter filter,
        CancellationToken cancellationToken = default);
}
