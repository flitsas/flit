using Flit.Tramites.Application.Identity;
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
    IKyverumCertificateClient certClient)
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

        if (!string.Equals(bio.Provider, BiometricProviders.Kyverum, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(bio.KyverumVerificationId))
            return (null, "sin_certificado");

        try
        {
            var cert = await certClient.DownloadCertificateAsync(bio.KyverumVerificationId!, ct);
            return cert is null
                ? (null, "sin_certificado")
                : (new CertificadoIdentidadResult(cert.Content, cert.ContentType, cert.FileName), null);
        }
        catch (KyverumCertificateException ex)
        {
            return (null, ex.Transient ? "proveedor_no_disponible" : "proveedor_error");
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
        if (!BiometricRules.EsAprobadaVigente(bio, DateTimeOffset.UtcNow))
            return false;

        var instance = await repo.GetByIdWithBiometricsAndActorsAsync(instanceId, tenantId, ct);
        if (instance is null)
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
