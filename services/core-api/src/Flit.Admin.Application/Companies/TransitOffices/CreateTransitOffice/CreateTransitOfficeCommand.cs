namespace Flit.Admin.Application.Companies.TransitOffices.CreateTransitOffice;

/// <summary>
/// Comando de alta de tenant OT: el payload validado + el operador que lo crea
/// (claim <c>sub</c> del JWT SuperAdmin), usado para <c>created_by</c>.
/// </summary>
public sealed class CreateTransitOfficeCommand
{
    public required CreateTransitOfficeRequest Request { get; init; }

    public Guid? CreatedBy { get; init; }
}
