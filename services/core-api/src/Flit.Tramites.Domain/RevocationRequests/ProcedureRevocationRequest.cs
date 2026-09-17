namespace Flit.Tramites.Domain.RevocationRequests;

/// <summary>
/// Estados de <see cref="ProcedureRevocationRequest"/> (tabla <c>tramites.procedure_revocation_requests</c>,
/// HU #12570). Reflejan el CHECK de BD (<c>ck_procedure_revocation_requests_status</c>).
/// </summary>
public static class ProcedureRevocationRequestStatus
{
    /// <summary>Solicitud recién creada; en manos del organismo de tránsito.</summary>
    public const string Solicitada = "solicitada";

    /// <summary>El organismo la tomó para revisión. Sigue contando como ACTIVA.</summary>
    public const string EnRevision = "en_revision";

    /// <summary>Decisión final: el organismo aprobó la revocatoria.</summary>
    public const string Aprobada = "aprobada";

    /// <summary>Decisión final: el organismo rechazó la revocatoria. Habilita un nuevo intento (AC5).</summary>
    public const string Rechazada = "rechazada";

    /// <summary>
    /// Estados ACTIVOS (a lo sumo uno por trámite, ver
    /// <c>uq_procedure_revocation_requests_active_per_instance</c> en la BD, AC4).
    /// </summary>
    public static readonly IReadOnlyList<string> Activos = [Solicitada, EnRevision];

    /// <summary>¿<paramref name="status"/> cuenta como solicitud activa (bloquea una nueva, AC4)?</summary>
    public static bool EsActivo(string? status) =>
        status is not null && Activos.Contains(status, StringComparer.Ordinal);
}

/// <summary>
/// HU #12570/#12571 (Feature #12565) — un intento de solicitud de revocatoria de un trámite Aprobado.
/// Una fila por intento (<see cref="AttemptNumber"/>); NUNCA sobrescribe ni reabre
/// <c>tramites.procedure_instances</c> (ADR-0022: el trámite permanece 'Aprobado' durante todo este
/// sub-flujo). Mapea 1:1 la tabla creada por la migración SQL cruda de HU #12570 (ver
/// <c>Persistence/Sql/Ddl/115-HU12570-procedure-revocation-requests.sql</c>).
/// </summary>
public sealed class ProcedureRevocationRequest
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProcedureInstanceId { get; set; }

    /// <summary>
    /// Número de intento para el mismo trámite, monotónico desde 1. Lo calcula
    /// <c>IProcedureRevocationRequestRepository.GetNextAttemptNumberAsync</c> (Domain/Repositories).
    /// </summary>
    public int AttemptNumber { get; set; }

    public string Status { get; set; } = ProcedureRevocationRequestStatus.Solicitada;
    public string? Reason { get; set; }
    public Guid? SupportDocumentId { get; set; }
    public Guid RequestedBy { get; set; }
    public DateTimeOffset RequestedAt { get; set; }

    public Guid? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionReason { get; set; }
    public Guid? DecisionDocumentId { get; set; }

    public long RowVersion { get; set; }

    /// <summary>¿Esta fila está ACTIVA (bloquea una nueva solicitud, AC4)?</summary>
    public bool EstaActiva => ProcedureRevocationRequestStatus.EsActivo(Status);
}
