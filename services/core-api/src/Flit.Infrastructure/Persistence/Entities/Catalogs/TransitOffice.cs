namespace Flit.Infrastructure.Persistence.Entities.Catalogs;

/// <summary>Organismo de tránsito — <c>catalogs.transit_offices</c> (HU #10152).</summary>
public sealed class TransitOffice
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string DepartmentCode { get; set; } = string.Empty;

    public string CityCode { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
