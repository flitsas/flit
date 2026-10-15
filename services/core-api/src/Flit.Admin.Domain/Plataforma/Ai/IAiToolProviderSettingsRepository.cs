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
