using Flit.Admin.Domain.Companies.Settings;
using Flit.Admin.Domain.Companies.SignatureVault;
using Flit.Tramites.Domain.Integration;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Implementación del puerto <see cref="ISignatureVaultPolicy"/> (HU #10643, ADR-0025 §4): ACTIVA el
/// flag hasta ahora inerte <c>signature_vault_enabled</c>. Lee la configuración operativa del tenant
/// vía <see cref="ITenantSettingsRepository"/> (<c>admin.tenant_operational_policies</c>, el mismo
/// resolver que usa el admin) y, si el baúl está habilitado, resuelve la firma <c>activa</c> de la
/// PERSONA por (tenant, tipo+número de documento) con
/// <see cref="ISignatureVaultReader.FindActiveByDocumentAsync"/> y enforce la vigencia con
/// <see cref="SignatureVault.EstaVigente"/> en fecha Colombia (UTC-5). HU #10930/#10937: el baúl es de
/// la persona (el representante legal seleccionado del actor jurídico), ya no del NIT de la compañía.
/// Mismo patrón que <see cref="IdentityValidationPolicy"/>/<see cref="RnmcRequirementPolicy"/>. NUNCA
/// devuelve material de firma: solo la referencia al artefacto (ADR-0025 §3).
/// </summary>
internal sealed class SignatureVaultPolicy : ISignatureVaultPolicy
{
    // Colombia no tiene horario de verano: UTC-5 fijo (coherente con BiometricRules).
    private static readonly TimeSpan ColombiaUtcOffset = TimeSpan.FromHours(-5);

    private readonly ITenantSettingsRepository _settings;
    private readonly ISignatureVaultReader _reader;

    public SignatureVaultPolicy(ITenantSettingsRepository settings, ISignatureVaultReader reader)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public async Task<SignatureVaultMatch?> ResolveAsync(
        Guid tenantId,
        string documentType,
        string documentNumber,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentType) || string.IsNullOrWhiteSpace(documentNumber))
        {
            return null;
        }

        // Baúl deshabilitado (o sin fila de config) → default seguro: no hay firma de baúl.
        var settings = await _settings.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (settings is not { SignatureVaultEnabled: true })
        {
            return null;
        }

        // Firma de la PERSONA (representante legal seleccionado del actor) por documento — HU #10930/#10937.
        var vault = await _reader
            .FindActiveByDocumentAsync(tenantId, documentType.Trim(), documentNumber.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (vault is null)
        {
            return null;
        }

        // Enforce de vigencia en fecha calendario Colombia (mismo criterio que la identidad).
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(ColombiaUtcOffset).Date);
        if (!vault.EstaVigente(today))
        {
            return null;
        }

        return new SignatureVaultMatch(
            vault.Id,
            vault.FullName,
            vault.SignatureHash,
            vault.StoragePath,
            vault.StorageSha256,
            vault.VigenciaDesde,
            vault.VigenciaHasta,
            vault.DocumentNumber,
            vault.CodigoHash);
    }
}
