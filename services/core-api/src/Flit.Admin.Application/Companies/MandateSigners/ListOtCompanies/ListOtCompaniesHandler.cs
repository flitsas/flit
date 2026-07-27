using Flit.Admin.Domain.Companies.MandateSigners;

namespace Flit.Admin.Application.Companies.MandateSigners.ListOtCompanies;

/// <summary>
/// Lista las compañías del OT con sus mandatarios activos resueltos (RF34, ADR-0036). Con la
/// MULTIPLICIDAD una compañía puede tener VARIOS mandatarios: se agrupan por compañía en
/// <c>AssignedSigners</c> (informativo para el multiselect; ya no se deshabilita por exclusividad).
/// Las compañías sin mandatario (lista vacía) son las advertidas por RF26.
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

        // ADR-0036 — MULTIPLICIDAD: una compañía puede tener VARIOS mandatarios activos. Se AGRUPA por
        // compañía (antes un ToDictionary por CompanyTenantId reventaba con clave duplicada en cuanto una
        // compañía tenía 2+ mandatarios).
        var signersByCompany = resolutions
            .GroupBy(r => r.CompanyTenantId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<AssignedSignerDto>)
                    [.. g.Select(r => new AssignedSignerDto(r.MandateSignerId, r.FullName, r.IntegrityHash))]);

        return
        [
            .. companies.Select(c => new OtCompanyResponse(
                c.CompanyTenantId,
                c.LegalName,
                c.IsActive,
                c.IsEnabled,
                signersByCompany.GetValueOrDefault(c.CompanyTenantId, []))),
        ];
    }
}
