using Flit.Admin.Domain.Companies.LegalRepresentatives;
using Flit.Admin.Domain.Companies.SignatureVault;

namespace Flit.Admin.Application.Companies.LegalRepresentatives.FindByNit;

/// <summary>
/// Caso de uso de la precarga por NIT del wizard (HU #10903, ADR-0033 §5.4). Si el tenant tiene un
/// representante ACTIVO para el NIT ingresado, devuelve la compañía + el representante + las banderas
/// de firma/identidad VIGENTES; si no hay match, <c>null</c> (el endpoint responde 404 y el FE cae a
/// RUES/RUNT).
///
/// Las banderas se calculan de forma INDEPENDIENTE (no excluyente, a diferencia del resolutor de
/// guardado): <see cref="FindRepresentativeByNitResponse.FirmaVigente"/> con la firma del baúl activa
/// por NIT (<see cref="ISignatureVaultReader.FindActiveByNitAsync"/> + <see cref="SignatureVault.EstaVigente"/>,
/// exigiendo que el documento del titular coincida con el del representante, como el resolutor) e
/// <see cref="FindRepresentativeByNitResponse.IdentidadVigente"/> con la validación de identidad
/// biométrica vigente por documento (<see cref="IRepresentativeIdentityLookup"/>, reuso HU #10350). El
/// "hoy"/"ahora" se ancla a la hora de Colombia (UTC-5) vía <see cref="TimeProvider"/>.
/// </summary>
public sealed class FindRepresentativeByNitHandler
{
    // Hora de Colombia (UTC-5, sin DST): la vigencia se cuenta por día calendario local (ADR-0025 §3).
    private static readonly TimeSpan ColombiaUtcOffset = TimeSpan.FromHours(-5);

    private readonly ILegalRepresentativeReader _representativeReader;
    private readonly ISignatureVaultReader _signatureVaultReader;
    private readonly IRepresentativeIdentityLookup _identityLookup;
    private readonly TimeProvider _timeProvider;

    public FindRepresentativeByNitHandler(
        ILegalRepresentativeReader representativeReader,
        ISignatureVaultReader signatureVaultReader,
        IRepresentativeIdentityLookup identityLookup,
        TimeProvider timeProvider)
    {
        _representativeReader = representativeReader ?? throw new ArgumentNullException(nameof(representativeReader));
        _signatureVaultReader = signatureVaultReader ?? throw new ArgumentNullException(nameof(signatureVaultReader));
        _identityLookup = identityLookup ?? throw new ArgumentNullException(nameof(identityLookup));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<FindRepresentativeByNitResponse?> HandleAsync(
        FindRepresentativeByNitQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(query.Nit))
        {
            return null;
        }

        var nit = query.Nit.Trim();

        var representative = await _representativeReader
            .FindActiveByCompanyNitAsync(query.TenantId, nit, cancellationToken)
            .ConfigureAwait(false);

        if (representative is null)
        {
            return null;
        }

        // Datos completos de la compañía (email/dirección/…); el read model del representante solo
        // denormaliza NIT + razón social.
        var company = await _representativeReader
            .FindRepresentedCompanyByNitAsync(query.TenantId, nit, cancellationToken)
            .ConfigureAwait(false);

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().ToOffset(ColombiaUtcOffset).DateTime);

        var firmaVigente = await ResolveFirmaVigenteAsync(query.TenantId, nit, representative, today, cancellationToken)
            .ConfigureAwait(false);

        var now = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), ColombiaUtcOffset);
        var identidadRef = await _identityLookup
            .FindVigenteIdentityRefAsync(
                query.TenantId, representative.DocumentType, representative.DocumentNumber, now, cancellationToken)
            .ConfigureAwait(false);

        var companyDto = new RepresentativeCompanyDto(
            company?.DocumentNumber ?? representative.CompanyDocumentNumber,
            company?.Name ?? representative.CompanyName,
            company?.Email,
            company?.Address,
            company?.City,
            company?.Phone);

        var contactDto = new RepresentativeContactDto(
            representative.DocumentType,
            representative.DocumentNumber,
            representative.Name,
            representative.FirstLastName,
            representative.SecondLastName,
            representative.Email,
            representative.Phone);

        return new FindRepresentativeByNitResponse(
            companyDto,
            contactDto,
            firmaVigente,
            IdentidadVigente: identidadRef is not null);
    }

    private async Task<bool> ResolveFirmaVigenteAsync(
        Guid tenantId,
        string nit,
        LegalRepresentativeItem representative,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var firma = await _signatureVaultReader
            .FindActiveByNitAsync(tenantId, nit, cancellationToken)
            .ConfigureAwait(false);

        // Firma vigente del baúl para el DOCUMENTO del representante (la consulta por NIT devuelve una
        // sola fila; el match por documento evita atribuir la firma de otro representante de la misma
        // compañía), coherente con LegalRepresentativeSignatureResolver.
        return firma is not null
            && firma.EstaVigente(today)
            && string.Equals(firma.DocumentNumber, representative.DocumentNumber, StringComparison.OrdinalIgnoreCase);
    }
}
