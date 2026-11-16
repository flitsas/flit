using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>Certificado (PDF) de una validación de identidad, listo para responder al cliente.</summary>
public sealed record CertificadoIdentidadResult(byte[] Content, string ContentType, string FileName);

/// <summary>
/// Descarga on-demand el certificado (PDF) de una validación de identidad desde Kyverum. Valida que la
/// validación pertenezca al tenant y sea la identidad EFECTIVA del trámite (propia o apalancada, HU
/// #11014), que sea del proveedor Kyverum y tenga id de verificación, y delega la descarga (login por
/// cookie) a <see cref="IKyverumCertificateClient"/>. A diferencia del FUR, aquí el fallo SÍ se reporta
/// al cliente (502/503) — es una descarga explícita.
/// </summary>
public sealed class DescargarCertificadoIdentidadHandler(
    IProcedureInstanceRepository repo,
    IKyverumCertificateClient certClient,
    IIdentitySignatureCapture? signatureCapture = null,
    IIdentityLetterService? letters = null,
    IAttachmentStorage? storage = null)
{
    public async Task<(CertificadoIdentidadResult? Result, string? Error)> HandleAsync(
        Guid instanceId, Guid tenantId, Guid validationId, CancellationToken ct = default)
    {
        var bio = await repo.GetBiometricByIdAsync(validationId, ct);
        if (bio is null || bio.TenantId != tenantId)
            return (null, "not_found");

        // HU #11014 — identidad APALANCADA (HU #10350): cuando la parte no tiene fila propia, el listado
        // expone la validación VIGENTE de la persona, que vive en OTRO trámite (o es una prevalidación
        // standalone, sin instancia). Exigir `ProcedureInstanceId == instanceId` devolvía 404 y la UI
        // pintaba "Validación de identidad no encontrada" con la identidad activa. Se acepta si es la
        // identidad efectiva de una parte de ESTE trámite: aprobada+vigente y con el documento del sujeto
        // de identidad de alguno de sus actores (el RL en persona jurídica, el actor en natural).
        if (bio.ProcedureInstanceId != instanceId
            && !await EsIdentidadEfectivaDelTramiteAsync(instanceId, tenantId, bio, ct).ConfigureAwait(false))
        {
            return (null, "not_found");
        }

        // HU #14302 (AC4) — validación MANUAL: el documento es la carta de FLIT (no hay proveedor que consultar). Mismas reglas de
        // pertenencia y tenant que arriba. Si la carta falta se genera aquí (la outbox pudo rendirse); si no se puede, error claro
        // para reintentar — nunca `proveedor_error`.
        if (string.Equals(bio.Provider, BiometricProviders.Manual, StringComparison.Ordinal))
            return await DescargarCartaAsync(bio, ct).ConfigureAwait(false);

        if (!string.Equals(bio.Provider, BiometricProviders.Kyverum, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(bio.KyverumVerificationId))
            return (null, "sin_certificado");

        try
        {
            var cert = await certClient.DownloadCertificateAsync(bio.KyverumVerificationId!, ct);
            if (cert is null)
                return (null, "sin_certificado");

            if (signatureCapture is not null)
            {
                var outcome = await signatureCapture.EnsureFromPdfAsync(bio, cert.Content, ct).ConfigureAwait(false);
                if (outcome == IdentitySignatureCaptureOutcome.Captured)
                    await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            return (new CertificadoIdentidadResult(cert.Content, cert.ContentType, cert.FileName), null);
        }
        catch (KyverumCertificateException ex)
        {
            return (null, ex.Transient ? "proveedor_no_disponible" : "proveedor_error");
        }
    }

    /// <summary>
    /// HU #14302 — descarga la carta de una validación MANUAL aprobada sin pasar por un trámite (prevalidación standalone o
    /// cualquier validación por id). <paramref name="tenantId"/> <c>null</c> = Super Admin (cualquier compañía; el tenant sale de la
    /// fila); con valor, solo se sirve la validación de ese tenant. Otro tenant, inexistente, no manual o no aprobada: <c>not_found</c>
    /// (no revela que existe). Si la carta no se puede obtener: <c>carta_no_disponible</c>.
    /// </summary>
    public async Task<(CertificadoIdentidadResult? Result, string? Error)> HandleCartaPorValidacionAsync(
        Guid validationId, Guid? tenantId, CancellationToken ct = default)
    {
        var bio = await repo.GetBiometricByIdAsync(validationId, ct).ConfigureAwait(false);
        if (bio is null || (tenantId is { } t && bio.TenantId != t) || !bio.CorrespondeCartaIdentidad)
            return (null, "not_found");

        return await DescargarCartaAsync(bio, ct).ConfigureAwait(false);
    }

    private async Task<(CertificadoIdentidadResult? Result, string? Error)> DescargarCartaAsync(
        ProcedureInstanceBiometricValidation bio, CancellationToken ct)
    {
        // Rechazada/en curso/aprobada sin origen manual: no hay carta que dar.
        if (!bio.CorrespondeCartaIdentidad)
            return (null, "sin_certificado");

        try
        {
            var conCarta = await IdentityLetterAccess.EnsureAsync(letters, repo, bio, ct).ConfigureAwait(false);
            if (conCarta is null || storage is null)
                return (null, "carta_no_disponible");

            var pdf = await IdentityLetterAccess.ReadPdfAsync(storage, conCarta, ct).ConfigureAwait(false);
            return pdf is null
                ? (null, "carta_no_disponible")
                : (new CertificadoIdentidadResult(pdf, "application/pdf", IdentityLetterAccess.FileName(conCarta)), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Storage caído, etc.: el usuario puede reintentar; no es un fallo del proveedor ni un 500.
            return (null, "carta_no_disponible");
        }
    }

    /// <summary>
    /// ¿La validación es la identidad EFECTIVA de alguna parte del trámite (HU #11014)? Debe estar
    /// aprobada y vigente y coincidir con el documento del sujeto de identidad de un actor. Mismo criterio
    /// que <c>ListBiometriaHandler</c> al exponer la identidad referenciada: si la UI la muestra, su
    /// certificado debe poder descargarse.
    /// </summary>
    private async Task<bool> EsIdentidadEfectivaDelTramiteAsync(
        Guid instanceId, Guid tenantId, ProcedureInstanceBiometricValidation bio, CancellationToken ct)
    {
        var instance = await repo.GetByIdWithBiometricsAndActorsAsync(instanceId, tenantId, ct);
        if (instance is null)
            return false;

        // HU #13824 — un trámite fuera de borrador conserva su certificado aunque la identidad se haya vencido a mano.
        var salidaDeBorrador = bio.ExpiredManuallyAt is null
            ? null
            : await IdentityApprovalResolver.SalidaDeBorradorAsync(repo, instance, ct);
        if (!BiometricRules.EsAprobadaVigenteParaTramite(bio, instance.Status, DateTimeOffset.UtcNow, salidaDeBorrador))
            return false;

        return instance.Actors.Any(a =>
        {
            var subject = IdentitySubjectResolver.For(a);
            return !string.IsNullOrWhiteSpace(subject.TipoDocumento)
                && !string.IsNullOrWhiteSpace(subject.NumeroDocumento)
                && BiometricRules.DocumentoCoincide(bio, subject.TipoDocumento, subject.NumeroDocumento);
        });
    }
}
