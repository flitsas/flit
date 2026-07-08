namespace Flit.Infrastructure.Persistence.Entities.Analytics;

/// <summary>
/// Informe programado — <c>analytics.report_schedules</c> (Reportes 2.0, HU-D §8 del contrato).
/// El scheduler (<c>AnalyticsSchedulerProcessor</c>) lo evalúa cada minuto en hora America/Bogota
/// y sella <see cref="LastSentAt"/> ANTES de enviar el correo (idempotencia multi-réplica).
/// Sin herencia de <c>TenantAuditableEntity</c> a propósito: la tabla del contrato NO tiene
/// row_version ni updated_by/deleted_by, y el modelo EF debe calcar el DDL (§1: la migración
/// se genera en integración a partir de estas configs).
/// </summary>
public sealed class ReportSchedule
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>resumen | operacion | ot | uso | productividad.</summary>
    public string ReportType { get; set; } = string.Empty;

    /// <summary>daily | weekly | monthly.</summary>
    public string Frequency { get; set; } = string.Empty;

    /// <summary>0 (domingo) … 6 (sábado); solo para frequency = weekly.</summary>
    public short? DayOfWeek { get; set; }

    /// <summary>1..28; solo para frequency = monthly.</summary>
    public short? DayOfMonth { get; set; }

    /// <summary>0..23, hora local America/Bogota.</summary>
    public short SendHour { get; set; } = 7;

    /// <summary>excel | pdf (formato solicitado; el correo lleva el resumen HTML, ver ADR en §8).</summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>Destinatarios (1..10 correos) persistidos como jsonb.</summary>
    public List<string> Recipients { get; set; } = [];

    public bool IsActive { get; set; } = true;

    /// <summary>Último envío sellado (UTC). Define la ventana ya cubierta (día/semana ISO/mes).</summary>
    public DateTimeOffset? LastSentAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Borrado lógico: no listado, no editable y el scheduler lo ignora.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
