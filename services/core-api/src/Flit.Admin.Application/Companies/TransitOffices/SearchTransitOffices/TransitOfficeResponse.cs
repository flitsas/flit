namespace Flit.Admin.Application.Companies.TransitOffices.SearchTransitOffices;

/// <summary>Organismo de tránsito expuesto por la API del catálogo (AC1).</summary>
public sealed record TransitOfficeResponse(
    Guid Id,
    string Code,
    string Name,
    string DepartmentCode,
    string CityCode);
