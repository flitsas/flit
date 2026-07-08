namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Parámetro documental de una compañía gestora (HU #10521, RF31) —
/// <c>admin.company_document_params</c>. Estado por tipo de documento:
/// <c>OCULTO | OBLIGATORIO | OPCIONAL</c>.
/// </summary>
public sealed class CompanyDocumentParamEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string DocumentTypeCode { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
