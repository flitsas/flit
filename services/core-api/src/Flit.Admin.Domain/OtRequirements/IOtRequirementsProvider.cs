namespace Flit.Admin.Domain.OtRequirements;

/// <summary>
/// Resuelve los requisitos efectivos de un OT (HU #10545). Devuelve los valores persistidos si
/// existen y, si no, <see cref="OtRequirements.SafeDefaults"/> (AC1): así el resto del sistema
/// consume siempre requisitos válidos sin ramas nulas.
/// </summary>
public interface IOtRequirementsProvider
{
    /// <summary>Requisitos efectivos del OT asociado al tenant (persistidos o defaults seguros).</summary>
    Task<OtRequirements> ResolveByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
