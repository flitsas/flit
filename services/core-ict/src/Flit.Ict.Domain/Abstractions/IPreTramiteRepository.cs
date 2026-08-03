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

    /// <summary>
    /// Carga un pre-trámite del tenant por su referencia pública, o null. La referencia puede ser el
    /// número secuencial que devuelve /register (<c>transaction_number</c>, paridad v1) o el
    /// <c>manager_id_transaction</c> propio del gestor; se prioriza el número cuando es numérica.
    /// </summary>
    Task<ExternalIntegrationMaster?> FindByManagerIdTransactionAsync(
        string reference,
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
    /// Encola un webhook 'anulado' (status_validation=6, ict_estado='anulado') al gestor para un
    /// pre-trámite que se anuló ANTES de materializar en core-api (paridad v1 abortProcess/WithNews).
    /// Los materializados NO usan esto: su webhook lo emite core-api por el Plano C (callback de estado).
    /// La observation vuelve al mismo gestor que la envió, por eso sí viaja en el mensaje (a diferencia
    /// del timeline, que la omite por PII).
    /// </summary>
    Task EnqueueAbortWebhookAsync(
        Guid masterId,
        Guid tenantId,
        string observation,
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
