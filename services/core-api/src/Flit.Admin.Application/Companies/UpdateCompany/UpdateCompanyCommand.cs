namespace Flit.Admin.Application.Companies.UpdateCompany;

/// <summary>
/// Comando de edición de compañía: el id del tenant + el payload validado + el operador
/// que la edita (claim <c>sub</c> del JWT SuperAdmin), usado para <c>updated_by</c>.
/// </summary>
public sealed class UpdateCompanyCommand
{
    public required Guid TenantId { get; init; }

    public required UpdateCompanyRequest Request { get; init; }

    public Guid? ChangedBy { get; init; }
}
