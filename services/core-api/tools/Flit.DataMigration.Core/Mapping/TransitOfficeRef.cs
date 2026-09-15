namespace Flit.DataMigration.V1.Mapping;

/// <summary>
/// Organismo de tránsito de V2 ya resuelto para un trámite (fila de <c>catalogs.transit_offices</c>).
/// Lo resuelve <c>TransitOfficeResolver</c> antes de mapear; el mapper solo lo copia.
/// </summary>
public sealed record TransitOfficeRef(
    Guid Id,
    string Code,
    string Name,
    string CityCode,
    string? CityName,
    bool IsActive);
