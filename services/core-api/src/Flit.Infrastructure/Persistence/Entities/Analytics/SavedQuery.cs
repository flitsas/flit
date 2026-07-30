namespace Flit.Infrastructure.Persistence.Entities.Analytics;

/// <summary>
/// Consultas guardadas por usuario — <c>analytics.saved_queries</c>.
/// (Feature #11076.) Privadas por defecto; <see cref="IsShared"/> = true comparte en el tenant.
/// </summary>
public sealed class SavedQuery
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Filtros serializados (mismo formato que export_jobs.filters_json).</summary>
    public string FiltersJson { get; set; } = "{}";

    /// <summary>false = solo el propietario la ve; true = visible a todos en el tenant.</summary>
    public bool IsShared { get; set; }

    // ── Columnas estándar A5 ──────────────────────────────────────────────
    public long RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}
