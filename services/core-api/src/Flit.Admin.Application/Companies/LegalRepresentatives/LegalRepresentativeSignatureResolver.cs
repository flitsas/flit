using Flit.Admin.Domain.Companies.SignatureVault;

namespace Flit.Admin.Application.Companies.LegalRepresentatives;

/// <summary>
/// Resolutor de firma/identidad al guardar un representante (HU #10900, ADR-0033 §5.1, D2).
/// Precedencia baúl &gt; identidad (coherente con <c>IdentityApprovalResolver</c>):
/// <list type="number">
///   <item>Firma del baúl ACTIVA + VIGENTE por NIT (<see cref="ISignatureVaultReader.FindActiveByNitAsync"/>
///     + <see cref="SignatureVault.EstaVigente"/>). Se exige además que el documento del titular de la
///     firma coincida con el del representante: el baúl se llavea por (tenant, NIT, documento), pero la
///     consulta por NIT devuelve una sola fila; el guardado por documento evita vincular la firma de
///     OTRO representante de la misma compañía.</item>
///   <item>Validación de identidad biométrica VIGENTE por documento
///     (<see cref="IRepresentativeIdentityLookup"/>, reuso HU #10350).</item>
///   <item>Ninguna → <see cref="LegalRepresentativeSignatureResolution.HasSignatureOrIdentity"/> = false.</item>
/// </list>
/// </summary>
public sealed class LegalRepresentativeSignatureResolver : ILegalRepresentativeSignatureResolver
{
    // Hora de Colombia (UTC-5, sin DST): la vigencia se cuenta por día calendario local (ADR-0025 §3
    // / BiometricRules). El instante "ahora" para la consulta de identidad se ancla al inicio del día
    // en Colombia, coherente con el corte por día de la vigencia biométrica.
    private static readonly TimeSpan ColombiaUtcOffset = TimeSpan.FromHours(-5);

    private readonly ISignatureVaultReader _signatureVaultReader;
    private readonly IRepresentativeIdentityLookup _identityLookup;

    public LegalRepresentativeSignatureResolver(
        ISignatureVaultReader signatureVaultReader,
        IRepresentativeIdentityLookup identityLookup)
    {
        _signatureVaultReader = signatureVaultReader ?? throw new ArgumentNullException(nameof(signatureVaultReader));
        _identityLookup = identityLookup ?? throw new ArgumentNullException(nameof(identityLookup));
    }

    public async Task<LegalRepresentativeSignatureResolution> ResolveAsync(
        Guid tenantId,
        string nitCompania,
        string tipoDocumento,
        string documentoRepresentante,
        DateOnly today,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nitCompania);
        ArgumentException.ThrowIfNullOrWhiteSpace(tipoDocumento);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentoRepresentante);

        var documento = documentoRepresentante.Trim();

        // (1) Firma del baúl activa + vigente de la PERSONA (por documento — HU #10932). El baúl se
        // deprecó del NIT: la firma es única de la persona en el tenant y aplica a todas sus compañías.
        var firma = await _signatureVaultReader
            .FindActiveByDocumentAsync(tenantId, tipoDocumento.Trim(), documento, cancellationToken)
            .ConfigureAwait(false);

        if (firma is not null && firma.EstaVigente(today))
        {
            return LegalRepresentativeSignatureResolution.FromSignature(firma.Id);
        }

        // (2) Validación de identidad biométrica vigente por documento.
        var now = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), ColombiaUtcOffset);
        var identityRef = await _identityLookup
            .FindVigenteIdentityRefAsync(tenantId, tipoDocumento.Trim(), documento, now, cancellationToken)
            .ConfigureAwait(false);

        if (identityRef is { } validationId)
        {
            return LegalRepresentativeSignatureResolution.FromIdentity(validationId);
        }

        // (3) Ninguna vigente.
        return LegalRepresentativeSignatureResolution.None;
    }
}
