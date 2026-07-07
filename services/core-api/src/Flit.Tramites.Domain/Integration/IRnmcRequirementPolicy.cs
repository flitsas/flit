namespace Flit.Tramites.Domain.Integration;

/// <summary>
/// Puerto para resolver si la consulta RNMC (medidas correctivas de la Policía) es EXIGIBLE para un
/// trámite, según la configuración del OT destino (<c>admin.ot_requirements.requires_rnmc</c>,
/// HU #10602). Desacopla el módulo de trámites del de Admin (mismo patrón que
/// <see cref="IIdentityValidationPolicy"/>).
/// </summary>
public interface IRnmcRequirementPolicy
{
    /// <summary>
    /// <c>true</c> = el OT destino exige RNMC. <c>false</c> = no lo exige (default seguro: RNMC no se
    /// corre salvo que el OT lo active). Si el OT no se puede resolver, devuelve <c>false</c>
    /// (a diferencia de identidad, que por seguridad devuelve <c>true</c>): RNMC nace desactivado y no
    /// debe bloquear trámites existentes salvo activación explícita del OT.
    /// </summary>
    Task<bool> IsRnmcRequiredAsync(
        Guid tenantId,
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementación que NUNCA exige RNMC — default seguro para tests de dominio/aplicación que no
/// ejercitan la configurabilidad por OT.
/// </summary>
public sealed class NullRnmcRequirementPolicy : IRnmcRequirementPolicy
{
    public static NullRnmcRequirementPolicy Instance { get; } = new();

    public Task<bool> IsRnmcRequiredAsync(
        Guid tenantId,
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
