namespace Flit.Infrastructure.Persistence.Entities.Analytics;

/// <summary>
/// SLA configurable por tipo de trámite y OT — <c>analytics.report_sla_config</c>.
/// (Feature #11076.) NULL en <see cref="TransitOfficeId"/> o <see cref="ProcedureType"/>
/// significa "aplica a todo el tenant" o "a todos los tipos", respectivamente.
/// </summary>
public sealed class ReportSlaConfig
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>NULL = aplica a todo el tenant (sin filtro por OT).</summary>
    public Guid? TransitOfficeId { get; set; }

    /// <summary>NULL = aplica a todos los tipos de trámite del tenant.</summary>
    public string? ProcedureType { get; set; }

    /// <summary>Horas hábiles objetivo. Ver <see cref="CalendarType"/>.</summary>
    public short SlaHours { get; set; }

    /// <summary>business = excluye festivos; calendar = días corridos.</summary>
    public string CalendarType { get; set; } = "business";

    public DateOnly EffectiveFrom { get; set; }

    public DateOnly? EffectiveTo { get; set; }

    // ── Columnas estándar A5 ──────────────────────────────────────────────
    public long RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}
