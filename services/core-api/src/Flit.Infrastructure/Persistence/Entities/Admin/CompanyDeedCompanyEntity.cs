namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Puente M:N escritura ↔ compañía representada — <c>admin.company_deed_companies</c> (HU #10900,
/// ADR-0033). Las compañías a las que aplica una escritura. Sin <c>tenant_id</c> propio: el
/// aislamiento por tenant se hereda de la escritura padre (RLS transitiva vía EXISTS).
/// </summary>
public sealed class CompanyDeedCompanyEntity
{
    public Guid Id { get; set; }

    public Guid DeedId { get; set; }

    public Guid RepresentedCompanyId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
