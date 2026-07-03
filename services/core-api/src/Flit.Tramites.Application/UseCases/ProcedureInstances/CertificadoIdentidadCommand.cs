using Flit.Tramites.Application.Identity;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>Certificado (PDF) de una validación de identidad, listo para responder al cliente.</summary>
public sealed record CertificadoIdentidadResult(byte[] Content, string ContentType, string FileName);

/// <summary>
/// Descarga on-demand el certificado (PDF) de una validación de identidad desde Kyverum. Valida que la
/// validación pertenezca al tenant y a la instancia (scoping), que sea del proveedor Kyverum y tenga id
/// de verificación, y delega la descarga (login por cookie) a <see cref="IKyverumCertificateClient"/>.
/// A diferencia del FUR, aquí el fallo SÍ se reporta al cliente (502/503) — es una descarga explícita.
/// </summary>
public sealed class DescargarCertificadoIdentidadHandler(
    IProcedureInstanceRepository repo,
    IKyverumCertificateClient certClient)
{
    public async Task<(CertificadoIdentidadResult? Result, string? Error)> HandleAsync(
        Guid instanceId, Guid tenantId, Guid validationId, CancellationToken ct = default)
    {
        var bio = await repo.GetBiometricByIdAsync(validationId, ct);
        if (bio is null || bio.TenantId != tenantId || bio.ProcedureInstanceId != instanceId)
            return (null, "not_found");

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
}
