using Flit.Ict.Domain.Entities;

namespace Flit.Ict.Domain.Abstractions;

/// <summary>
/// Persistencia de pre-trámites (<c>ict.external_integration_master</c> + actores). Las
/// implementaciones fijan el GUC <c>app.current_tenant_id</c> (RLS) por unidad de trabajo.
/// </summary>
public interface IPreTramiteRepository
{
    /// <summary>Inserta un pre-trámite con sus actores. Devuelve el id generado.</summary>
    Task<Guid> AddAsync(ExternalIntegrationMaster master, Guid tenantId, CancellationToken ct = default);

    /// <summary>Carga un pre-trámite (con actores) del tenant por su id, o null.</summary>
    Task<ExternalIntegrationMaster?> GetAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>Carga un pre-trámite por su manager_id_transaction (el TransactionFlit del cliente), o null.</summary>
    Task<ExternalIntegrationMaster?> FindByManagerIdTransactionAsync(
        string managerIdTransaction,
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>Persiste cambios de un pre-trámite ya rastreado (edición). Lanza si hay conflicto de row_version.</summary>
    Task SaveAsync(Guid tenantId, CancellationToken ct = default);
}
