using Flit.Admin.Application.Companies.MandateSigners.AssociableCompanies;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Queries.Domain.Time;

namespace Flit.Admin.Application.Companies.MandateSigners.ListMandateSigners;

/// <summary>Lista los mandatarios activos de un OT con sus compañías (RF27).</summary>
public sealed class ListMandateSignersHandler
{
    private readonly IMandateSignerReader _reader;
    private readonly IManagingCompanyDirectory? _companies;

    public ListMandateSignersHandler(IMandateSignerReader reader, IManagingCompanyDirectory? companies = null)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _companies = companies;
    }

    public async Task<IReadOnlyList<MandateSignerResponse>> HandleAsync(
        ListMandateSignersQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var signers = await _reader
            .ListByOtAsync(query.TransitOfficeId, query.Visibility, cancellationToken).ConfigureAwait(false);

        // HU #13179b — id, nombre y NIT de las asociadas, en una sola consulta (sin N+1).
        var officeCompanies = await AssociatedCompaniesEnricher
            .EnrichAsync(signers, _companies, cancellationToken).ConfigureAwait(false);

        var today = ColombiaTime.Today(TimeProvider.System);

        return
        [
            .. signers.Select(s => new MandateSignerResponse(
                s.Id,
                s.TransitOfficeId,
                s.FullName,
                s.DocumentType,
                s.DocumentNumber,
                s.IntegrityHash,
                s.Email,
                s.UserId,
                s.IdentityValidationRef,
                s.SignatureVaultId,
                s.IdentityStatus,
                s.RegisteredAt,
                s.IsActive,
                s.CompanyTenantIds,
                s.TransitOfficeIds,
                null,
                officeCompanies[s.Id],
                s.SignerModel,
                s.SignatureMethod,
                s.ValidityKind,
                s.ValidFrom,
                s.ValidTo,
                s.ValidityStatusOn(today),
                s.FirmaValidezOn(today)?.Valida,
                s.FirmaValidezOn(today)?.Motivo)),
        ];
    }
}
