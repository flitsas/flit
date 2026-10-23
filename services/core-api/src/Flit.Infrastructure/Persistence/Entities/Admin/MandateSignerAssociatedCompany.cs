namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Compañía de FLIT (por tenant) a la que un mandatario se asocia en un organismo (HU #13177, nivel 3 de la
/// prelación). Distinta de <see cref="MandateSignerCompany"/> (compañía PROPIETARIA, nivel 2) y de la tabla
/// legada <see cref="MandateSignerRepresentedCompany"/> (fichas de Representante Legal, sin uso).
/// Un mandatario sin filas aplica solo a su propia compañía.
/// </summary>
public sealed class MandateSignerAssociatedCompany
{
    public Guid Id { get; set; }
    public Guid MandateSignerId { get; set; }
    public Guid TransitOfficeId { get; set; }
    public Guid AssociatedCompanyTenantId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}
