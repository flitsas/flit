namespace Flit.Admin.Application.GeneracionDocumental.Ports;

/// <summary>
/// Resultado normalizado de la consulta RUES por NIT.
/// </summary>
/// <param name="Found">
/// <c>true</c> solo si el RUES devolvió razón social. Un NIT sin coincidencia llega con
/// <c>Found = false</c> y sin error: no es una caída del proveedor.
/// </param>
/// <param name="Error">
/// <c>provider_unavailable</c> (el proveedor no respondió) o <c>provider_not_found</c> (no está
/// registrado). <c>null</c> en el camino normal. Nunca transporta la excepción cruda.
/// </param>
public sealed record StandaloneRuesLookupResult(
    bool Found,
    IReadOnlyDictionary<string, string?> Fields,
    DateTimeOffset QueriedAt,
    string? Error);

/// <summary>
/// Puerto acotado de la consulta RUES (persona jurídica por NIT) para el módulo standalone. El
/// adaptador de Infrastructure reusa <c>RuesActorJuridicalLookup</c> —el mismo núcleo del wizard—
/// con <c>Guid.Empty</c> como instancia, de modo que no conviven dos resoluciones del proveedor.
/// La consulta es EN VIVO y efímera: este puerto no persiste nada (CF-04).
/// </summary>
public interface IStandaloneRuesCompanyLookup
{
    Task<StandaloneRuesLookupResult> ConsultAsync(
        Guid tenantId,
        string nit,
        CancellationToken cancellationToken = default);
}
