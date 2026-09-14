namespace Flit.Tramites.Application.BulkTramites.Processing;

/// <summary>
/// Directorio de representantes legales de la empresa, visto desde la carga masiva (HU #12538).
/// Es la MISMA precarga por NIT que usa el paso de actores del wizard
/// (<c>GET /tramites/legal-representatives/lookup</c>, <c>FindRepresentativeByNitHandler</c>);
/// existe como puerto porque ese caso de uso vive en <c>Flit.Admin.Application</c>, que este
/// módulo no referencia. Devuelve null cuando el tenant no tiene representantes activos para el NIT.
/// </summary>
public interface IBulkTramitesLegalRepresentativeDirectory
{
    Task<BulkTramitesCompanyDirectoryEntry?> FindByNitAsync(Guid tenantId, string nit, CancellationToken ct);
}

/// <summary>
/// Compañía del directorio con sus representantes activos, el primario de primero (mismo orden
/// que el selector del wizard). El contacto de la compañía es el que el wizard aplica al actor
/// cuando precarga desde el directorio.
/// </summary>
public sealed record BulkTramitesCompanyDirectoryEntry(
    string? Email,
    string? Direccion,
    string? Ciudad,
    string? Telefono,
    IReadOnlyList<BulkTramitesLegalRepresentative> Representantes);

/// <summary>Representante legal del directorio. <c>NumeroDocumento</c> es PII: no loguear.</summary>
public sealed record BulkTramitesLegalRepresentative(
    string TipoDocumento,
    string NumeroDocumento,
    string NombreCompleto,
    string? Email,
    string? Telefono);
