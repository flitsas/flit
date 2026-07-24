namespace Flit.Tramites.Domain.Entities;

/// <summary>
/// Estado de autorización (Habeas Data, Ley 1581) de una persona para que sus datos consultados en
/// un trámite se reutilicen en OTROS trámites del mismo tenant — HU #10878 (Feature #10862, CF-04),
/// ADR-0031. Gate leído por <c>ExternalQueryCacheService</c> antes de servir un HIT de
/// <see cref="ExternalQueryCacheEntry"/> de tipo persona. 1 fila por <c>(tenant, documento)</c>.
///
/// <para>FAIL-SAFE: la AUSENCIA de fila, o <see cref="Status"/> distinto de
/// <see cref="PersonDataConsentStatus.Granted"/>, se trata siempre como "no reutilizable" — nunca
/// bloquea el trámite, solo desactiva la optimización de reúso para esa persona.</para>
/// </summary>
public sealed class PersonDataConsent
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    public string DocumentType { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;

    /// <summary><see cref="PersonDataConsentStatus"/>.</summary>
    public string Status { get; set; } = PersonDataConsentStatus.Unknown;

    public string? ConsentVersion { get; set; }

    /// <summary>De dónde vino la autorización (p. ej. <c>actor_capture_v1</c>).</summary>
    public string? ConsentSource { get; set; }

    public DateTimeOffset? GrantedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Prueba de auditoría (mismo patrón que <see cref="ProcedureInstanceParticipant"/>).</summary>
    public string? CapturedIp { get; set; }
    public string? CapturedUserAgent { get; set; }

    /// <summary>Trámite donde se capturó la autorización (trazabilidad, opcional).</summary>
    public Guid? SourceProcedureInstanceId { get; set; }

    public long RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>Valores válidos de <see cref="PersonDataConsent.Status"/> (CHECK en BD).</summary>
public static class PersonDataConsentStatus
{
    public const string Granted = "granted";
    public const string Revoked = "revoked";
    public const string Unknown = "unknown";
}

/// <summary>Reglas/constantes de captura del consentimiento (ADR-0031, HU #10878).</summary>
public static class PersonDataConsentRules
{
    /// <summary>Versión vigente del "consentimiento de reúso cross-trámite" (versionado para auditoría).</summary>
    public const string ConsentVersion = "2026-07-23-v1";

    /// <summary>Origen de captura vía <c>PUT actors</c> (HU #10878; UI de captura llega en HU aparte, #10885).</summary>
    public const string ConsentSourceActorCapture = "actor_capture_v1";
}
