namespace Flit.Admin.Application.Companies.TransitOffices.GetTransitGrants;

/// <summary>
/// Respuesta del listado de grants habilitados (AC5): ids de organismos habilitados.
/// </summary>
public sealed record TransitGrantsResponse(IReadOnlyList<Guid> TransitOfficeIds);
