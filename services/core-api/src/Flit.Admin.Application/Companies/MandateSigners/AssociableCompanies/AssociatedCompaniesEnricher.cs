using Flit.Admin.Domain.Companies.MandateSigners;

namespace Flit.Admin.Application.Companies.MandateSigners.AssociableCompanies;

/// <summary>
/// HU #13179b (Feature #13119 F7) — completa, para la LECTURA de mandatarios, el id + nombre + NIT de cada compañía
/// asociada, con UNA sola consulta al directorio de compañías gestoras (<see cref="IManagingCompanyDirectory"/>),
/// sin N+1 por mandatario ni por organismo. Los campos existentes (<c>associatedCompanyTenantIds</c>) se conservan:
/// solo se agrega <c>associatedCompanies</c>. Expone únicamente id, nombre y NIT (Ley 1581).
/// </summary>
internal static class AssociatedCompaniesEnricher
{
    /// <summary>
    /// Devuelve las asociadas enriquecidas por mandatario. Sin directorio o sin asociaciones devuelve las originales.
    /// Un id que ya no es compañía gestora se queda solo en <c>associatedCompanyTenantIds</c>.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<MandateSignerOfficeCompanies>>> EnrichAsync(
        IReadOnlyList<MandateSignerItem> signers,
        IManagingCompanyDirectory? directory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signers);

        var result = new Dictionary<Guid, IReadOnlyList<MandateSignerOfficeCompanies>>();
        foreach (var s in signers)
        {
            result[s.Id] = s.OfficeCompanies;
        }

        if (directory is null)
        {
            return result;
        }

        var ids = signers
            .SelectMany(s => s.OfficeCompanies)
            .SelectMany(o => o.AssociatedCompanyTenantIds ?? [])
            .Distinct()
            .ToList();
        if (ids.Count == 0)
        {
            return result;
        }

        var rows = (await directory.ListByIdsAsync(ids, cancellationToken).ConfigureAwait(false))
            .ToDictionary(r => r.Id, r => new AssociableCompany(r.Id, r.Name, r.Nit));

        foreach (var s in signers)
        {
            result[s.Id] =
            [
                .. s.OfficeCompanies.Select(o => o with
                {
                    AssociatedCompanies =
                    [
                        .. (o.AssociatedCompanyTenantIds ?? [])
                            .Where(rows.ContainsKey)
                            .Select(id => rows[id]),
                    ],
                }),
            ];
        }

        return result;
    }
}
