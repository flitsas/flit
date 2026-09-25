using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.MandateSigners.ListOtCompanies;

/// <summary>
/// Compañías gestoras del OT con su mandatario asignado (RF34, vista consolidada + insumo del
/// multiselect del formulario con exclusividad).
/// </summary>
public sealed class ListOtCompaniesQuery
{
    public required Guid TransitOfficeId { get; init; }

    /// <summary>Bug #12912 (Ley 1581) — ver <see cref="OtCompanyVisibility"/>.</summary>
    public OtCompanyVisibility Visibility { get; init; } = OtCompanyVisibility.WholeNetwork;
}
