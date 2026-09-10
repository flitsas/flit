using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.RuntConfirmation;

namespace Flit.Tramites.Application.UseCases.RuntConfirmation;

/// <summary>Un trámite tal como lo ve la corrida: lo mínimo para consultar y evaluar.</summary>
public sealed record RuntConfirmationCandidate(
    Guid InstanceId,
    Guid TenantId,
    string ReferenceNumber,
    string ProcedureTypeCode,
    ProcedureFamily Family,
    string? Vin,
    string? Plate,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    string? TransitOfficeName,
    RuntDocument? Owner,
    RuntDocument? Seller,
    RuntDocument? Buyer,
    int RuntAttempts,
    DateTimeOffset? RuntConfirmedAt,
    string? RuntFlag)
{
    /// <summary>Fecha de corte del motor: envío al OT (radicación). Sin ella, la aprobación; sin ninguna, la creación.</summary>
    public DateTimeOffset CutoffAt => SubmittedAt ?? ApprovedAt ?? CreatedAt;
}

public sealed record RuntDocument(string Type, string Number);

/// <summary>Filtros del universo de la corrida (HU #12309 AC2), calculados desde la configuración.</summary>
public sealed record RuntConfirmationUniverseFilter(
    int MaxAttempts,
    int GraceDays,
    DateTimeOffset NowUtc,
    int Take = 5000);

/// <summary>Cambios que un intento deja en el trámite. Solo toca las columnas <c>runt_*</c>, nunca el status.</summary>
public sealed record RuntConfirmationInstanceUpdate(
    bool IncrementAttempts,
    DateTimeOffset? ConfirmedAt,
    string? Flag,
    bool ClearFlag);

/// <summary>Persistencia de corridas, intentos, crudos y la marca del trámite.</summary>
public interface IRuntConfirmationStore
{
    Task<IReadOnlyList<RuntConfirmationCandidate>> ListUniverseAsync(RuntConfirmationUniverseFilter filter, CancellationToken ct = default);

    /// <summary>Trámite por id sin los filtros del universo (para «Consultar ahora» y re-evaluación). NULL si no existe o está borrado.</summary>
    Task<RuntConfirmationCandidate?> GetCandidateAsync(Guid instanceId, CancellationToken ct = default);

    /// <summary>Guarda el crudo (sanitizado) en <c>external_query_payloads</c>. NULL si el JSON no es guardable.</summary>
    Task<Guid?> SaveRawPayloadAsync(Guid tenantId, Guid instanceId, string providerKey, string? subjectKey, string rawJson, DateTimeOffset queriedAt, CancellationToken ct = default);

    Task<string?> GetPayloadJsonAsync(Guid payloadId, CancellationToken ct = default);

    /// <summary>Respuesta cruda del vehículo guardada al radicar (la más antigua anterior al envío al OT). NULL si no hay.</summary>
    Task<string?> GetBaselinePayloadJsonAsync(Guid instanceId, DateTimeOffset cutoffAt, CancellationToken ct = default);

    Task<RuntConfirmationRun> StartRunAsync(RuntConfirmationRun run, CancellationToken ct = default);
    Task FinishRunAsync(RuntConfirmationRun run, CancellationToken ct = default);

    /// <summary>¿Hay una corrida con <c>finished_at</c> NULL más reciente que <paramref name="staleAfter"/>?</summary>
    Task<bool> IsRunInProgressAsync(TimeSpan staleAfter, CancellationToken ct = default);

    /// <summary>
    /// Inicio de la última corrida PROGRAMADA, para decidir si hoy ya tocó. Con <paramref name="includeSkipped"/>
    /// cuentan también las saltadas por interruptor apagado (una marca por día); sin él, solo las que consultaron.
    /// </summary>
    Task<DateTimeOffset?> GetLastScheduledRunStartedAtAsync(bool includeSkipped, CancellationToken ct = default);

    /// <summary>Inserta el intento y aplica <paramref name="update"/> al trámite en la misma unidad de trabajo.</summary>
    Task RecordAttemptAsync(RuntConfirmationAttempt attempt, RuntConfirmationInstanceUpdate update, CancellationToken ct = default);

    Task<RuntConfirmationAttempt?> GetAttemptAsync(Guid attemptId, CancellationToken ct = default);

    /// <summary>El intento que una corrida dejó para un trámite (para devolverlo tras «Consultar ahora»).</summary>
    Task<RuntConfirmationAttempt?> GetLatestAttemptForRunAsync(Guid runId, Guid instanceId, CancellationToken ct = default);

    /// <summary><c>status</c> actual del trámite, o NULL si no existe.</summary>
    Task<string?> GetProcedureStatusAsync(Guid instanceId, CancellationToken ct = default);
}

/// <summary>Cómo se consulta al proveedor. Exactamente una de las dos formas.</summary>
public sealed record RuntRawQuery(string? Vin, string? Plate, RuntDocument? Document)
{
    public static RuntRawQuery ByVin(string vin) => new(vin, null, null);
    public static RuntRawQuery ByPlate(string plate, RuntDocument document) => new(null, plate, document);
    public string SubjectKey => Vin ?? Plate ?? string.Empty;
}

public enum RuntRawOutcome
{
    /// <summary>El proveedor devolvió el vehículo (crudo completo).</summary>
    Found,

    /// <summary>El proveedor dijo que no existe (o que el documento no es del propietario). Hay crudo (real o sintético).</summary>
    NotFound,

    /// <summary>Timeout, 5xx, red, respuesta ilegible: NO es veredicto, no consume intento.</summary>
    Error,
}

public sealed record RuntRawQueryResult(RuntRawOutcome Outcome, string? RawJson, string? Message)
{
    public static RuntRawQueryResult Error(string message) => new(RuntRawOutcome.Error, null, message);
}

/// <summary>
/// Consumidor PROPIO del cliente RUNT según el proveedor configurado. No pasa por
/// <c>IConsultationProviderChainResolver</c> ni por el override por tenant: la Confirmación RUNT
/// elige su proveedor en su configuración global, no en la de la empresa.
/// </summary>
public interface IRuntVehicleRawClient
{
    Task<RuntRawQueryResult> ConsultAsync(string providerKey, RuntRawQuery query, CancellationToken ct = default);
}
