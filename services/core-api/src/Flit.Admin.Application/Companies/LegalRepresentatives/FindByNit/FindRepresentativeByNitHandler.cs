using Flit.Admin.Domain.Companies.LegalRepresentatives;
using Flit.Admin.Domain.Companies.SignatureVault;

namespace Flit.Admin.Application.Companies.LegalRepresentatives.FindByNit;

/// <summary>
/// Caso de uso de la precarga por NIT del wizard (HU #10903/#10937, ADR-0033 §5.4). Si el tenant tiene
/// uno o más representantes ACTIVOS para el NIT ingresado, devuelve la compañía + TODOS los
/// representantes (<see cref="ISignatureVaultReader"/>) con sus banderas de firma/identidad VIGENTES; si
/// no hay match, <c>null</c> (el endpoint responde 404 y el FE cae a RUES/RUNT). Cuando hay varios, el
/// FE ofrece un selector para elegir cuál se precarga y firma (HU #10937).
///
/// Las banderas se calculan de forma INDEPENDIENTE (no excluyente, a diferencia del resolutor de
/// guardado) POR CADA representante y por su DOCUMENTO:
/// <see cref="RepresentativeOptionDto.FirmaVigente"/> con la firma del baúl activa de la persona
/// (<see cref="ISignatureVaultReader.FindActiveByDocumentAsync"/> + <see cref="SignatureVault.EstaVigente"/>,
/// HU #10930 — el baúl es de la persona, ya no del NIT) e
/// <see cref="RepresentativeOptionDto.IdentidadVigente"/> con la validación de identidad biométrica
/// vigente por documento (<see cref="IRepresentativeIdentityLookup"/>, reuso HU #10350). El "hoy"/"ahora"
/// se ancla a la hora de Colombia (UTC-5) vía <see cref="TimeProvider"/>.
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

        // TODOS los representantes activos de la compañía (HU #10937): base del selector cuando hay varios.
        var representatives = await _representativeReader
            .ListActiveByCompanyNitAsync(query.TenantId, nit, cancellationToken)
            .ConfigureAwait(false);

        if (representatives.Count == 0)
        {
            return null;
        }

        // Datos completos de la compañía (email/dirección/…); el read model del representante solo
        // denormaliza NIT + razón social.
        var company = await _representativeReader
            .FindRepresentedCompanyByNitAsync(query.TenantId, nit, cancellationToken)
            .ConfigureAwait(false);

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().ToOffset(ColombiaUtcOffset).DateTime);
        var now = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), ColombiaUtcOffset);

        var options = new List<RepresentativeOptionDto>(representatives.Count);
        foreach (var representative in representatives)
        {
            var firmaVigente = await ResolveFirmaVigenteAsync(query.TenantId, representative, today, cancellationToken)
                .ConfigureAwait(false);

            var identidadRef = await _identityLookup
                .FindVigenteIdentityRefAsync(
                    query.TenantId, representative.DocumentType, representative.DocumentNumber, now, cancellationToken)
                .ConfigureAwait(false);

            options.Add(new RepresentativeOptionDto(
                representative.DocumentType,
                representative.DocumentNumber,
                representative.Name,
                representative.FirstLastName,
                representative.SecondLastName,
                representative.Email,
                representative.Phone,
                firmaVigente,
                IdentidadVigente: identidadRef is not null));
        }

        var primary = representatives[0];
        var companyDto = new RepresentativeCompanyDto(
            company?.DocumentNumber ?? primary.CompanyDocumentNumber,
            company?.Name ?? primary.CompanyName,
            company?.Email,
            company?.Address,
            company?.City,
            company?.Phone);

        var contactDto = new RepresentativeContactDto(
            primary.DocumentType,
            primary.DocumentNumber,
            primary.Name,
            primary.FirstLastName,
            primary.SecondLastName,
            primary.Email,
            primary.Phone);

        return new FindRepresentativeByNitResponse(
            companyDto,
            contactDto,
            // Compat: las banderas de nivel superior reflejan al representante primario (el primero).
            options[0].FirmaVigente,
            options[0].IdentidadVigente,
            options);
    }

    private async Task<bool> ResolveFirmaVigenteAsync(
        Guid tenantId,
        LegalRepresentativeItem representative,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        // Firma vigente del baúl de la PERSONA (por su documento) — HU #10930: el baúl es de la persona,
        // ya no del NIT; cada representante firma con su propia firma. Coherente con
        // LegalRepresentativeSignatureResolver.
        var firma = await _signatureVaultReader
            .FindActiveByDocumentAsync(
                tenantId, representative.DocumentType, representative.DocumentNumber, cancellationToken)
            .ConfigureAwait(false);

        return firma is not null && firma.EstaVigente(today);
    }
}
