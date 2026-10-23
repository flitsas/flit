namespace Flit.Admin.Domain.Plataforma.Ai;

/// <summary>Persistencia de settings y transiciones manuales (HU #13509).</summary>
public interface IAiToolProviderSettingsRepository
{
    Task<IReadOnlyList<AiToolProviderSettings>> ListAsync(CancellationToken cancellationToken = default);

    Task<AiToolProviderSettings?> GetByToolCodeAsync(
        string toolCode,
        CancellationToken cancellationToken = default);

    Task<AiToolProviderSettings?> SaveSettingsAsync(
        string toolCode,
        bool autoReturnToPrimary,
        IReadOnlyList<AiProviderSlot> providers,
        Guid? updatedBy,
        CancellationToken cancellationToken = default);

    /// <summary>Cambia <c>active_provider</c> e inserta fila append-only en <c>ai_provider_transitions</c>.</summary>
    Task<(ActivateProviderResult Result, AiToolProviderSettings? Settings)> ActivateProviderAsync(
        string toolCode,
        string providerCode,
        Guid? actorUserId,
        string? detail,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Failover automático (HU #13510): actualiza <c>active_provider</c>, inserta transición
    /// <c>reason=failover_auto</c> y fila en <c>ai_failover_alerts</c>.
    /// </summary>
    Task RecordFailoverAsync(
        string toolCode,
        string fromProvider,
        string toProvider,
        string? detail,
        CancellationToken cancellationToken = default);

    /// <summary>Historial paginado de transiciones por tool (HU #13512), <c>occurred_at DESC</c>.</summary>
    Task<(IReadOnlyList<AiProviderTransition> Items, long TotalCount)> ListTransitionsAsync(
        string toolCode,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Alertas de failover; <paramref name="acked"/> filtra por presencia de <c>acked_at</c>.</summary>
    Task<IReadOnlyList<AiFailoverAlert>> ListAlertsAsync(
        bool? acked,
        CancellationToken cancellationToken = default);

    /// <summary>Marca alerta como leída. Devuelve <c>null</c> si no existe.</summary>
    Task<AiFailoverAlert?> AcknowledgeAlertAsync(
        Guid alertId,
        Guid? ackedBy,
        CancellationToken cancellationToken = default);
}

public enum ActivateProviderResult
{
    Ok,
    ToolNotFound,
    UnknownProvider,
    ProviderDisabled,
    ProviderNotCapable,
    ProviderNoApiKey,
    Unchanged,
}
