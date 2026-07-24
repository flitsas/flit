namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Representante legal de una compañía — <c>admin.company_legal_representatives</c> (HU #10900,
/// ADR-0033). Registro = par (compañía representada + representante), tenant-scoped (RLS por
/// <c>tenant_id</c>). Vincula la firma del baúl (<c>SignatureVaultId</c>, ON DELETE SET NULL) o la
/// validación de identidad vigente (<c>IdentityValidationRef</c>), resueltas al guardar.
/// <c>DocumentNumber</c> es PII (@pii:high, Ley 1581): no loguear ni exponer en errores.
/// </summary>
public sealed class CompanyLegalRepresentativeEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid RepresentedCompanyId { get; set; }

    public string DocumentType { get; set; } = string.Empty;

    /// <summary>Documento del representante. PII (@pii:high): no loguear.</summary>
    public string DocumentNumber { get; set; } = string.Empty;

    public string FirstLastName { get; set; } = string.Empty;

    public string? SecondLastName { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? Address { get; set; }

    public string? City { get; set; }

    public string? Phone { get; set; }

    /// <summary>FK opcional → <c>admin.signature_vault</c> (firma del baúl vigente). ON DELETE SET NULL.</summary>
    public Guid? SignatureVaultId { get; set; }

    /// <summary>Id de la validación biométrica vigente vinculada (reuso de identidad). Nullable.</summary>
    public Guid? IdentityValidationRef { get; set; }

    public bool IsActive { get; set; } = true;

    public long RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
