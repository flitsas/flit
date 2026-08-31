using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Integration;

namespace Flit.Tramites.Application.Documents;

/// <summary>
/// Resuelve CÓMO se estampa la firma del MANDATARIO en el Contrato Privado de Mandato (HU #11030):
/// imagen del baúl si la tiene → sello de su validación de identidad vigente → nada (línea en blanco).
///
/// <para><b>Por qué es compartido.</b> Esta cadena vivía únicamente dentro de
/// <c>FurCommand.TryGenerateMandatoAsync</c>. El simulador de mandatos (Feature #11702) tiene que
/// mostrar el documento <i>tal como saldría</i>, y una segunda copia de la precedencia habría
/// divergido en cuanto una de las dos cambiara: el simulador diría "así se firma" mostrando algo que
/// el trámite no emite. Ambos llaman aquí.</para>
///
/// <para>El documento del firmante es PII (Ley 1581): no se loguea. Un fallo leyendo el baúl NO
/// interrumpe la generación —el contrato sale con el sello de identidad o con la línea—, y por eso el
/// llamador solo recibe el aviso por <paramref name="onVaultError"/>.</para>
/// </summary>
public static class MandatarioFirmaResolver
{
    /// <summary>Firma resuelta: imagen del baúl, o sello de texto, o ninguna de las dos.</summary>
    public readonly record struct Resultado(byte[]? Firma, string? Sello, FirmaBaulMetadata? Metadatos);

    public static async Task<Resultado> ResolveAsync(
        ISignatureVaultPolicy vaultPolicy,
        IAttachmentStorage storage,
        Guid tenantId,
        MandateSignerCandidate signer,
        Action<Exception>? onVaultError = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vaultPolicy);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(signer);

        var tipoDoc = string.IsNullOrWhiteSpace(signer.TipoDocumento) ? "CC" : signer.TipoDocumento!.Trim();

        try
        {
            var match = await vaultPolicy
                .ResolveMandatarioAsync(tenantId, tipoDoc, signer.Documento.Trim(), cancellationToken)
                .ConfigureAwait(false);

            if (match is not null && !string.IsNullOrWhiteSpace(match.StoragePath))
            {
                var stream = await storage.OpenReadAsync(match.StoragePath, cancellationToken)
                    .ConfigureAwait(false);
                if (stream is not null)
                {
                    await using (stream.ConfigureAwait(false))
                    {
                        using var ms = new MemoryStream();
                        await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
                        if (ms.Length > 0)
                        {
                            // HU #11170 — la imagen viaja con su trazabilidad (vigencia y hash), igual
                            // que la de las partes: una firma estampada que no se puede verificar
                            // leyendo el documento no sirve de nada.
                            var metadatos = new FirmaBaulMetadata(
                                match.DocumentNumber,
                                match.FullName,
                                match.VigenciaDesde,
                                match.VigenciaHasta,
                                match.SignatureVaultId,
                                match.CodigoHash);
                            return new Resultado(ms.ToArray(), null, metadatos);
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            onVaultError?.Invoke(ex);
        }

        // Sin firma del baúl: sello con el certificado de su identidad vigente, si lo hay.
        return signer.IdentityVigente && !string.IsNullOrWhiteSpace(signer.CertificadoIdentidad)
            ? new Resultado(null, $"Validación de identidad\nFirma {signer.CertificadoIdentidad}", null)
            : new Resultado(null, null, null);
    }
}
