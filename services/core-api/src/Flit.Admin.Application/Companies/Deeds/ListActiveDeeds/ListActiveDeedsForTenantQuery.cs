namespace Flit.Admin.Application.Companies.Deeds.ListActiveDeeds;

/// <summary>
/// Consulta de las escrituras activas y vigentes de un tenant para el consumo del wizard (HU #10903).
/// El tenant lo impone el <c>TenantEnforcementMiddleware</c> desde el JWT del operador (no del body).
/// </summary>
public sealed class ListActiveDeedsForTenantQuery
{
    public required Guid TenantId { get; init; }
}
