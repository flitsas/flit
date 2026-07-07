using Flit.Admin.Domain.Companies.MandateSigners;

namespace Flit.Admin.Application.Companies.MandateSigners.ListOtCompanies;

/// <summary>
/// Lista las compañías del OT con su mandatario activo resuelto (RF34). El multiselect del
/// formulario usa <c>AssignedSigner*</c> para deshabilitar y etiquetar las compañías ya
/// tomadas por otro mandatario; las de mandatario nulo son las advertidas por RF26.
/// </summary>
public sealed class ListOtCompaniesHandler
{
    private readonly IMandateSignerReader _reader;

    public ListOtCompaniesHandler(IMandateSignerReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public async Task<IReadOnlyList<OtCompanyResponse>> HandleAsync(
        ListOtCompaniesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var companies = await _reader
            .ListOtCompaniesAsync(query.TransitOfficeId, cancellationToken).ConfigureAwait(false);
        var resolutions = await _reader
            .ListActiveCompanyResolutionsAsync(query.TransitOfficeId, cancellationToken)
            .ConfigureAwait(false);

        var resolutionByCompany = resolutions.ToDictionary(r => r.CompanyTenantId);

        return
        [
            .. companies.Select(c =>
            {
                var resolution = resolutionByCompany.GetValueOrDefault(c.CompanyTenantId);
                return new OtCompanyResponse(
                    c.CompanyTenantId,
                    c.LegalName,
                    c.IsActive,
                    c.IsEnabled,
                    resolution?.MandateSignerId,
                    resolution?.FullName,
                    resolution?.IntegrityHash);
            }),
        ];
    }
}
