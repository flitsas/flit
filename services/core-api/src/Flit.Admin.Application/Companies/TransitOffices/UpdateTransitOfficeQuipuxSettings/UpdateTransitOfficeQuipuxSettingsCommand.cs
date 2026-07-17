namespace Flit.Admin.Application.Companies.TransitOffices.UpdateTransitOfficeQuipuxSettings;

/// <summary>
/// Comando de parametrización Quipux de una secretaría del catálogo (HU #10710). Solo
/// SuperAdmin: el catálogo es global y su carga es manual, secretaría por secretaría.
/// </summary>
public sealed class UpdateTransitOfficeQuipuxSettingsCommand
{
    /// <summary>Oficina del catálogo — <c>catalogs.transit_offices.id</c>.</summary>
    public required Guid TransitOfficeId { get; init; }

    /// <summary>Estado destino de la parametrización.</summary>
    public required UpdateTransitOfficeQuipuxSettingsRequest Request { get; init; }
}
