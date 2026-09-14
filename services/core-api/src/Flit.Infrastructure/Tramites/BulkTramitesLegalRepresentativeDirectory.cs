using Flit.Admin.Application.Companies.LegalRepresentatives.FindByNit;
using Flit.Tramites.Application.BulkTramites.Processing;

namespace Flit.Infrastructure.Tramites;

/// <summary>
/// Implementación del puerto <see cref="IBulkTramitesLegalRepresentativeDirectory"/> (HU #12538)
/// sobre <see cref="FindRepresentativeByNitHandler"/>, el MISMO caso de uso que atiende la precarga
/// por NIT del wizard (<c>GET /tramites/legal-representatives/lookup</c>). No decide nada: traduce
/// el resultado al vocabulario de la carga masiva y compone el nombre del representante igual que
/// lo hace el paso de actores (nombres + primer apellido + segundo apellido).
/// </summary>
internal sealed class BulkTramitesLegalRepresentativeDirectory(FindRepresentativeByNitHandler handler)
    : IBulkTramitesLegalRepresentativeDirectory
{
    public async Task<BulkTramitesCompanyDirectoryEntry?> FindByNitAsync(
        Guid tenantId, string nit, CancellationToken ct)
    {
        var result = await handler
            .HandleAsync(new FindRepresentativeByNitQuery { TenantId = tenantId, Nit = nit }, ct)
            .ConfigureAwait(false);

        if (result is null)
        {
            return null;
        }

        var representantes = result.Representantes
            .Select(r => new BulkTramitesLegalRepresentative(
                string.IsNullOrWhiteSpace(r.TipoDoc) ? "CC" : r.TipoDoc.Trim(),
                r.Documento,
                NombreCompleto(r.Nombres, r.PrimerApellido, r.SegundoApellido),
                r.Email,
                r.Telefono))
            .ToList();

        return new BulkTramitesCompanyDirectoryEntry(
            result.Company.Email,
            result.Company.Address,
            result.Company.City,
            result.Company.Phone,
            representantes);
    }

    private static string NombreCompleto(string nombres, string primerApellido, string? segundoApellido) =>
        string.Join(' ', new[] { nombres, primerApellido, segundoApellido }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim()));
}
