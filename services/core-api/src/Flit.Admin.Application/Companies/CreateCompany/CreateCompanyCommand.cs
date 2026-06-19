namespace Flit.Admin.Application.Companies.CreateCompany;

/// <summary>
/// Comando de alta de compañía: el payload validado + el operador que la crea
/// (claim <c>sub</c> del JWT SuperAdmin), usado para <c>created_by</c>.
/// </summary>
public sealed class CreateCompanyCommand
{
    public required CreateCompanyRequest Request { get; init; }

    public Guid? CreatedBy { get; init; }
}
