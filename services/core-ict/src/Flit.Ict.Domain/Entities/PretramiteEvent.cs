namespace Flit.Ict.Domain.Entities;

/// <summary>
/// Evento del timeline de negocio de un pre-trámite (<c>ict.pretramite_events</c>, HU5 / E1).
/// <see cref="Detail"/> viene SANITIZADO (allowlist, sin PII ni tokens). Lo escribe la función
/// <c>ict.record_pretramite_event</c>; esta entidad es para la LECTURA futura del timeline.
/// </summary>
public sealed class PretramiteEvent
{
    public Guid Id { get; set; }

    public Guid MasterId { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>Etapa (vocabulario IctEstado: recibido, en_validacion_negocio, …).</summary>
    public string Stage { get; set; } = string.Empty;

    /// <summary>Resultado de la etapa: ok | con_novedades | advertencia | error.</summary>
    public string Outcome { get; set; } = string.Empty;

    /// <summary>Detalle sanitizado (jsonb).</summary>
    public string? Detail { get; set; }

    public Guid? CorrelationId { get; set; }

    public DateTime CreatedAt { get; set; }
}
