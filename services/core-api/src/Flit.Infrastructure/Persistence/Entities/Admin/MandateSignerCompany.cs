namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Asignación mandatario↔compañía gestora dentro de un organismo de tránsito (ADR-0023). El
/// par <c>(transit_office_id, company_tenant_id)</c> tiene como máximo un mandatario activo,
/// garantizado por un índice único parcial filtrado por <c>is_active</c>.
///
/// <c>IsActive</c> refleja el estado del mandatario padre: al inactivar el mandatario sus
/// filas quedan <c>is_active = false</c> (se conservan como histórico) y dejan de contar para
/// el índice, liberando la compañía para reasignación.
/// </summary>
public sealed class MandateSignerCompany
{
    public Guid Id { get; set; }
    public Guid MandateSignerId { get; set; }

    /// <summary>Denormalizado para el índice de exclusividad por OT.</summary>
    public Guid TransitOfficeId { get; set; }
    public Guid CompanyTenantId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}
