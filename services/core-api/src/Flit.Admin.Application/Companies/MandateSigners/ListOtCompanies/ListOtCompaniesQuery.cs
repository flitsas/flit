namespace Flit.Admin.Application.Companies.MandateSigners.ListOtCompanies;

/// <summary>
/// Compañías gestoras del OT con su mandatario asignado (RF34, vista consolidada + insumo del
/// multiselect del formulario con exclusividad).
/// </summary>
public sealed class ListOtCompaniesQuery
{
    public required Guid TransitOfficeId { get; init; }
}
