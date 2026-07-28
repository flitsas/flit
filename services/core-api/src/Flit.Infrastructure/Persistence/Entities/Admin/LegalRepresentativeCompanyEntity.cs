namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Puente M:N representante ↔ compañía representada — <c>admin.legal_representative_companies</c>
/// (HU #10932, Feature #10929, RL-flujo-ajustes). Permite que un representante (persona, única por
/// <c>(tenant, documento)</c>) tenga VARIAS compañías, con una sola firma/identidad a nivel persona.
/// Tenant-scoped (RLS por <c>tenant_id</c>), mismo patrón que <c>company_deed_companies</c>.
/// </summary>
public sealed class LegalRepresentativeCompanyEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid RepresentativeId { get; set; }

    public Guid RepresentedCompanyId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }
}
