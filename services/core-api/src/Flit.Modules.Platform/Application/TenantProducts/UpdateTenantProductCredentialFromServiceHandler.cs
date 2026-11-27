using System.Text.RegularExpressions;
using Flit.Modules.Platform.Domain.TenantProducts;

namespace Flit.Modules.Platform.Application.TenantProducts;

/// <summary>
/// HU #14694 (M04b, D-ALTA-IDEM, plan §5.8; <c>CredencialesProductoService.ActualizarCredencialImpuestoDiagnostico</c>) — cuando el
/// alta del proveedor rota la llave de la empresa (<c>api_key_rotated</c>), la anterior deja de servir también en Diagnóstico:
/// el servicio de Impuestos propaga la nueva y este handler la cifra y REEMPLAZA la fila <c>diagnostico/impuesto_api_key</c> que
/// ya existe. Nunca la crea (sin fila devuelve false), no mira si Diagnóstico está encendido y no lee el valor anterior.
/// </summary>
/// <remarks>
/// La forma de la llave es la misma de la escritura REST (<see cref="SetTenantProductCredentialHandler"/>): texto visible de 16 a
/// 256 caracteres. El reemplazo y su auditoría (con el cliente de servicio, sin valor ni huella) van en el mismo guardado del
/// repositorio. El valor nunca entra a una excepción ni a un log.
/// </remarks>
public sealed partial class UpdateTenantProductCredentialFromServiceHandler
{
    private const int ProtectedMinLength = 16;
    private const int ProtectedMaxLength = 4000;

    private readonly ITenantProductCredentialRepository _credentials;
    private readonly ITenantProductSecretProtector _protector;

    public UpdateTenantProductCredentialFromServiceHandler(ITenantProductCredentialRepository credentials, ITenantProductSecretProtector protector)
    {
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

    /// <param name="tenantId">Empresa (la de <c>x-flit-tenant-id</c>).</param>
    /// <param name="value">La llave nueva del impuesto.</param>
    /// <param name="servicio">Cliente de servicio que la propaga (p. ej. <c>svc-impuestos</c>); va a la auditoría.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>true si reemplazó la fila; false si la empresa no tenía llave de Diagnóstico.</returns>
    public async Task<bool> HandleAsync(Guid tenantId, string? value, string servicio, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(servicio);
        if (value is null || !ValorValido().IsMatch(value))
            throw new TenantProductException(TenantProductException.CredentialInvalid, SetTenantProductCredentialHandler.InvalidValueMessage);

        var protectedValue = _protector.Protect(tenantId, TenantProductCredentialCodes.ImpuestoApiKey, value);
        if (protectedValue.Length is < ProtectedMinLength or > ProtectedMaxLength)
            throw new InvalidOperationException("El cifrado de la credencial quedó fuera del rango permitido.");

        return await _credentials.ReplaceExistingFromServiceAsync(
                tenantId,
                TenantProductCredentialCodes.Diagnostico,
                TenantProductCredentialCodes.ImpuestoApiKey,
                protectedValue,
                servicio,
                cancellationToken)
            .ConfigureAwait(false);
    }

    // \z (no $): en .NET `$` también hace match antes de un \n final.
    [GeneratedRegex(@"^[\x21-\x7E]{16,256}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ValorValido();
}
