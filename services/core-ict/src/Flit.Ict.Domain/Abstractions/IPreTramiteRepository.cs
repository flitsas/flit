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

    /// <summary>
    /// Marca el pre-trámite como anulado (process_status_id=6) y registra la fila 'ANULADO' en el
    /// histórico (para que el endpoint de estado v1 refleje la anulación). Idempotente por estado.
    /// </summary>
    Task MarkAbortedAsync(
        Guid masterId,
        Guid tenantId,
        string observation,
        string user,
        string mail,
        string company,
        CancellationToken ct = default);

    /// <summary>
    /// Emite un evento al timeline de negocio (<c>ict.pretramite_events</c>) vía la función
    /// <c>ict.record_pretramite_event</c>. <paramref name="detailJson"/> ya viene SANITIZADO (allowlist,
    /// sin PII). Best-effort desde el punto de vista del llamador.
    /// </summary>
    Task RecordTimelineEventAsync(
        Guid masterId,
        Guid tenantId,
        string stage,
        string outcome,
        string? detailJson,
        CancellationToken ct = default);
}
