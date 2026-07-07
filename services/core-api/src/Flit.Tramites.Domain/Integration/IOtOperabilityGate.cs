namespace Flit.Tramites.Domain.Integration;

/// <summary>
/// Puerto para validar que un organismo de tránsito esté OPERATIVO a nivel plataforma
/// (HU #10518), independiente del grant de la empresa (<see cref="ITransitOfficeGrantGate"/>).
/// Un OT es operativo cuando: la oficina del catálogo está activa, existe su
/// <c>transit_office_profile</c> + tenant OT, y el tenant OT tiene <c>is_active = true</c>.
/// Desacopla trámites del módulo Admin (la implementación vive en Infrastructure).
/// </summary>
public interface IOtOperabilityGate
{
    /// <summary>
    /// Indica si el organismo <paramref name="transitOfficeId"/> está operativo en FLIT
    /// (catálogo activo + perfil/tenant OT existente + tenant OT activo).
    /// </summary>
    Task<bool> IsOperableAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementación permisiva (siempre operativo) para cuando no hay validación de
/// operabilidad cableada — p. ej. en tests de dominio/aplicación que no la ejercitan.
/// </summary>
public sealed class NullOtOperabilityGate : IOtOperabilityGate
{
    public static NullOtOperabilityGate Instance { get; } = new();

    public Task<bool> IsOperableAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}
