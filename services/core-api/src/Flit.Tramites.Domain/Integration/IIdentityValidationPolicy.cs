namespace Flit.Tramites.Domain.Integration;

/// <summary>
/// Puerto para resolver si la validación biométrica de identidad es EXIGIBLE para un trámite,
/// según la configuración del OT destino (<c>admin.ot_requirements.identity_validation_enabled</c>,
/// HU #10548). Desacopla el módulo de trámites del de Admin.
/// </summary>
public interface IIdentityValidationPolicy
{
    /// <summary>
    /// <c>true</c> = se exige identidad aprobada/vigente (default seguro). <c>false</c> = el OT
    /// destino la tiene deshabilitada por acuerdo. Si el OT no se puede resolver, devuelve
    /// <c>true</c> (comportamiento seguro por defecto, AC2).
    /// </summary>
    Task<bool> IsIdentityValidationRequiredAsync(
        Guid tenantId,
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementación permisiva que SIEMPRE exige identidad — default seguro para tests de
/// dominio/aplicación que no ejercitan la configurabilidad por OT.
/// </summary>
public sealed class NullIdentityValidationPolicy : IIdentityValidationPolicy
{
    public static NullIdentityValidationPolicy Instance { get; } = new();

    public Task<bool> IsIdentityValidationRequiredAsync(
        Guid tenantId,
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}
