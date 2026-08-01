using Flit.Admin.Application.Companies.MandateSigners.ListMandateSigners;
using Flit.Admin.Domain.Companies.MandateSigners;

namespace Flit.Admin.Application.Companies.MandateSigners.ListCompanyMandateSigners;

/// <summary>Organismo de tránsito ofrecido a la compañía al elegir dónde aplica un mandatario.</summary>
public sealed record CompanyTransitOfficeResponse(Guid TransitOfficeId, string Code, string Name);

/// <summary>
/// HU #11202 — mandatarios vistos desde la COMPAÑÍA gestora. Es la vista inversa de la consola del
/// organismo: desde este cambio el alta la hace la empresa y marca en qué organismos aplica, no al
/// revés.
/// </summary>
public sealed class ListCompanyMandateSignersHandler
{
    private readonly IMandateSignerReader _reader;

    public ListCompanyMandateSignersHandler(IMandateSignerReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public async Task<IReadOnlyList<MandateSignerResponse>> HandleAsync(
        Guid companyTenantId,
        CancellationToken cancellationToken = default)
    {
        var signers = await _reader
            .ListByCompanyAsync(companyTenantId, cancellationToken).ConfigureAwait(false);

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

/// <summary>
/// HU #11202 (AC2) — organismos que la compañía puede elegir. Solo los que tiene habilitados: registrar
/// un mandatario en un organismo donde no puede radicar no serviría de nada.
/// </summary>
public sealed class ListCompanyTransitOfficesHandler
{
    private readonly IMandateSignerReader _reader;

    public ListCompanyTransitOfficesHandler(IMandateSignerReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public async Task<IReadOnlyList<CompanyTransitOfficeResponse>> HandleAsync(
        Guid companyTenantId,
        CancellationToken cancellationToken = default)
    {
        var options = await _reader
            .ListCompanyTransitOfficesAsync(companyTenantId, cancellationToken).ConfigureAwait(false);

        return [.. options.Select(o => new CompanyTransitOfficeResponse(o.TransitOfficeId, o.Code, o.Name))];
    }
}
