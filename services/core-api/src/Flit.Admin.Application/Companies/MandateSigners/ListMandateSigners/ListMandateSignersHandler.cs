using Flit.Admin.Domain.Companies.MandateSigners;

namespace Flit.Admin.Application.Companies.MandateSigners.ListMandateSigners;

/// <summary>Lista los mandatarios activos de un OT con sus compañías (RF27).</summary>
public sealed class ListMandateSignersHandler
{
    private readonly IMandateSignerReader _reader;

    public ListMandateSignersHandler(IMandateSignerReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public async Task<IReadOnlyList<MandateSignerResponse>> HandleAsync(
        ListMandateSignersQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var signers = await _reader
            .ListByOtAsync(query.TransitOfficeId, cancellationToken).ConfigureAwait(false);

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
                s.TransitOfficeIds)),
        ];
    }
}
