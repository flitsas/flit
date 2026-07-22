namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Grant que habilita un tipo de trámite a una compañía (tenant) —
/// <c>admin.company_procedure_type_grants</c> (FEATURE-08). Modelo grant: la fila = habilitado.
/// RLS por <c>app.current_tenant_id</c>. Unicidad por <c>(tenant_id, procedure_type_id)</c>.
/// </summary>
public sealed class CompanyProcedureTypeGrant
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid ProcedureTypeId { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }
}
