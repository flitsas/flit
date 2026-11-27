using Flit.Modules.Platform.Domain.TenantProducts;

namespace Flit.Modules.Platform.Application.TenantProducts;

/// <summary>
/// HU #14694 (D-CRED-DIAG, DA-2=b, DA-3 residual; <c>CredencialesProductoService.ObtenerCredencialImpuestoParaCopia</c>) —
/// entrega la llave <c>diagnostico/impuesto_api_key</c> de la empresa para que el servicio de otro producto la copie a su
/// propio almacén al aprovisionar. A diferencia de <see cref="GetTenantProductCredentialHandler"/>, NO exige Diagnóstico
/// encendido (no recibe el acceso por producto) ni toca la credencial.
/// </summary>
/// <remarks>
/// Cada lectura, con o sin fila, queda en <c>admin.tenant_config_audit_logs</c> (sin valor ni huella) ANTES de descifrar: si
/// la auditoría falla, no se entrega la llave.
/// </remarks>
public sealed class GetTenantProductCredentialForCopyHandler
{
    private readonly ITenantProductCredentialRepository _credentials;
    private readonly ITenantProductSecretProtector _protector;

    public GetTenantProductCredentialForCopyHandler(ITenantProductCredentialRepository credentials, ITenantProductSecretProtector protector)
    {
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

    /// <param name="tenantId">Empresa.</param>
    /// <param name="lector">Cliente de servicio que lee (p. ej. <c>svc-impuestos</c>); va a la auditoría.</param>
    public async Task<TenantProductCredentialDelivery> HandleAsync(Guid tenantId, string lector, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lector);

        var row = await _credentials.GetAsync(
                tenantId,
                TenantProductCredentialCodes.Diagnostico,
                TenantProductCredentialCodes.ImpuestoApiKey,
                cancellationToken)
            .ConfigureAwait(false);

        await _credentials.RegistrarLecturaParaCopiaAsync(
                tenantId,
                TenantProductCredentialCodes.Diagnostico,
                TenantProductCredentialCodes.ImpuestoApiKey,
                lector,
                row is not null,
                cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
            return TenantProductCredentialDelivery.Unavailable(MotivoCredencialImpuesto.SinConfigurar);

        var value = _protector.Unprotect(tenantId, TenantProductCredentialCodes.ImpuestoApiKey, row.SecretProtected);
        return TenantProductCredentialDelivery.Available(value, row.RowVersion);
    }
}
