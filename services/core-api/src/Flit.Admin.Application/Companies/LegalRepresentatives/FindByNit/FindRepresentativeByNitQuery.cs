namespace Flit.Admin.Application.Companies.LegalRepresentatives.FindByNit;

/// <summary>
/// Consulta de precarga del wizard (HU #10903): representante legal del tenant por NIT ingresado. El
/// tenant lo impone el <c>TenantEnforcementMiddleware</c> desde el JWT del operador (no del cliente).
/// <c>Nit</c> es PII (Ley 1581): no loguear.
/// </summary>
public sealed class FindRepresentativeByNitQuery
{
    public required Guid TenantId { get; init; }

    public required string Nit { get; init; }
}
