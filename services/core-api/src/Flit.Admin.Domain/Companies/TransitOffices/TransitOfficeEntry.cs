namespace Flit.Admin.Domain.Companies.TransitOffices;

/// <summary>
/// Entrada del catálogo estático de organismos de tránsito (HU #10192, RF13).
/// Refleja una fila lógica de <c>catalogs.transit_offices</c> pero <em>no</em> se
/// consulta vía EF: el catálogo vive en memoria (sin sync externo) para el listado.
/// </summary>
public sealed record TransitOfficeEntry(
    Guid Id,
    string Code,
    string Name,
    string DepartmentCode,
    string CityCode);
