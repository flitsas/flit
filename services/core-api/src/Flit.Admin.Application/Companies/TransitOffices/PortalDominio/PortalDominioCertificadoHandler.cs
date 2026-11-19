using Flit.Admin.Application.Companies.Domains.Verification;
using Flit.Admin.Domain.Companies.Domains;
using Flit.Admin.Domain.Companies.TransitOffices.PortalDominio;
using Microsoft.Extensions.Logging;

namespace Flit.Admin.Application.Companies.TransitOffices.PortalDominio;

public enum PortalDominioCertificadoResultado
{
    /// <summary>El host no es el dominio de ningún portal (puede ser de marca blanca).</summary>
    NoEsDePortal,
    Actualizado,
    NoVerificado,
}

/// <summary>
/// HU #14382/#14384 — señal de certificado del borde sobre el dominio del portal. Certificado emitido y TXT comprobado ⇒ el
/// dominio pasa a <c>active</c> sin activación manual de red. Si el certificado no se puede emitir queda el motivo, se registra
/// una alerta y el dominio NO se activa (<c>verified → failed</c>; el trabajo de comprobación lo reintenta con retroceso).
/// </summary>
public sealed partial class PortalDominioCertificadoHandler(
    IPortalDominioRepository repo, DomainVerificationOptions opciones, ILogger<PortalDominioCertificadoHandler> logger, TimeProvider? reloj = null)
{
    public const string Trabajo = "job:certificate-issuer";
    private const int MotivoMaxLargo = 300;

    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;

    public async Task<PortalDominioCertificadoResultado> EmitidoAsync(string host, DateTimeOffset emitidoEn, DateTimeOffset? venceEn, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (await repo.ObtenerPorHostAsync(host, ct).ConfigureAwait(false) is not { } actual)
            return PortalDominioCertificadoResultado.NoEsDePortal;

        if (PortalDominioEstados.DecidirCertificado(actual.Status, _reloj.GetUtcNow()) is not { } decision)
            return PortalDominioCertificadoResultado.NoVerificado;

        return await repo.AplicarCertificadoAsync(host, decision.NewStatus, decision.ActivatedAt, emitidoEn, venceEn, Trabajo, ct).ConfigureAwait(false) is null
            ? PortalDominioCertificadoResultado.NoEsDePortal
            : PortalDominioCertificadoResultado.Actualizado;
    }

    public async Task<PortalDominioCertificadoResultado> FalloAsync(string host, string? detalle, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (await repo.ObtenerPorHostAsync(host, ct).ConfigureAwait(false) is not { } actual)
            return PortalDominioCertificadoResultado.NoEsDePortal;

        var ahora = _reloj.GetUtcNow();
        var motivo = ArmarMotivo(detalle);
        Alerta(logger, actual.TransitOfficeId, actual.Host, motivo);

        // Una renovación fallida no tumba un dominio que sigue sirviendo con su certificado vigente: solo queda la alerta.
        if (actual.Status != TenantDomainStatus.Verified)
            return actual.Status == TenantDomainStatus.Active ? PortalDominioCertificadoResultado.Actualizado : PortalDominioCertificadoResultado.NoVerificado;

        await repo.MarcarCertificadoFallidoAsync(host, motivo, ahora, ahora + opciones.InitialRetryInterval, Trabajo, ct).ConfigureAwait(false);
        return PortalDominioCertificadoResultado.Actualizado;
    }

    private static string ArmarMotivo(string? detalle)
    {
        var limpio = new string((detalle ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
        var motivo = limpio.Length == 0 ? PortalDominioErrores.CertificadoNoEmitido : $"{PortalDominioErrores.CertificadoNoEmitido}: {limpio}";
        return motivo.Length <= MotivoMaxLargo ? motivo : motivo[..MotivoMaxLargo];
    }

    [LoggerMessage(Level = LogLevel.Error,
        Message = "ALERTA: no se pudo emitir el certificado del dominio del portal {TransitOfficeId} ({Host}): {Motivo}. El dominio no se activa.")]
    private static partial void Alerta(ILogger logger, Guid transitOfficeId, string host, string motivo);
}
