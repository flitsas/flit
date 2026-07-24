namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Puente M:N representante ↔ tipo de trámite —
/// <c>admin.company_legal_representative_procedure_types</c> (HU #10900, ADR-0033). Marca qué tipos
/// de trámite puede firmar el representante. Sin <c>tenant_id</c> propio: el aislamiento por tenant
/// se hereda del representante padre (RLS transitiva vía EXISTS).
/// </summary>
public sealed class CompanyLegalRepresentativeProcedureTypeEntity
{
    public Guid Id { get; set; }

    public Guid RepresentativeId { get; set; }

    public Guid ProcedureTypeId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
