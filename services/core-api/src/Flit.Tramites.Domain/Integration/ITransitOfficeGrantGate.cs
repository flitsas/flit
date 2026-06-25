namespace Flit.Tramites.Domain.Integration;

/// <summary>
/// Puerto para validar que un organismo de tránsito esté HABILITADO para la empresa
/// (admin.tenant_transit_office_grants) sin acoplar el módulo de trámites al de Admin.
/// </summary>
public interface ITransitOfficeGrantGate
{
    /// <summary>
    /// Indica si el organismo <paramref name="transitOfficeId"/> está habilitado para el
    /// tenant <paramref name="tenantId"/> (existe un grant activo).
    /// </summary>
    Task<bool> IsEnabledForTenantAsync(
        Guid tenantId,
        Guid transitOfficeId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementación permisiva (siempre habilitado) para cuando no hay validación de grants
/// cableada — p. ej. en tests de dominio/aplicación que no ejercitan la restricción por empresa.
/// </summary>
public sealed class NullTransitOfficeGrantGate : ITransitOfficeGrantGate
{
    public static NullTransitOfficeGrantGate Instance { get; } = new();

    public Task<bool> IsEnabledForTenantAsync(
        Guid tenantId,
        Guid transitOfficeId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}
