namespace Flit.Admin.Domain.Companies.MandateSigners;

/// <summary>
/// Ajuste HU #13123 (Epic #13090, F1) — puerto de lectura para el alta de mandatario desde el OT: ¿la
/// persona tiene una validación biométrica APROBADA y VIGENTE en el tenant de la compañía destino? Es el
/// mismo criterio de tenant que HU #13121 (la validación vive en el tenant de la compañía, no en el del
/// organismo) y la misma regla de vigencia del módulo Identidad. Una validación en curso o vencida NO
/// cuenta. <c>documentNumber</c> es PII (Ley 1581): no loguear.
/// </summary>
public interface IMandateSignerBiometricApprovalReader
{
    Task<bool> HasApprovedValidAsync(
        Guid companyTenantId,
        string documentType,
        string documentNumber,
        CancellationToken cancellationToken = default);
}
