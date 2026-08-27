namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Compañía representada — <c>admin.represented_companies</c> (HU #10900, ADR-0033). Dimensión por
/// NIT, tenant-scoped (RLS por <c>tenant_id</c>), que comparten representantes y escrituras.
/// <c>DocumentNumber</c> (NIT) es PII (Ley 1581): no loguear ni exponer en errores.
/// </summary>
public sealed class RepresentedCompanyEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string DocumentType { get; set; } = "NIT";

    /// <summary>NIT de la compañía. PII (@pii:medium): no loguear.</summary>
    public string DocumentNumber { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? Address { get; set; }

    public string? City { get; set; }

    public string? Phone { get; set; }

    /// <summary>Dueño de la ficha (un RL). Null en filas huérfanas (p. ej. solo mandatario).</summary>
    public Guid? RepresentativeId { get; set; }

    public bool IsActive { get; set; } = true;

    public long RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
