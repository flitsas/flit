namespace Flit.Admin.Domain.DocumentRequirements;

/// <summary>
/// Guard de integridad para el borrado de una asociación trámite ↔ documento
/// (HU #10195, AC4). Bloquea el DELETE cuando la asociación ya está en uso en
/// trámites activos con snapshot.
///
/// La implementación real llega con la HU #10197 (instancias de trámite); hasta
/// entonces se usa un stub que reporta "no en uso" para permitir el borrado.
/// </summary>
public interface IProcedureDocumentRequirementUsageGuard
{
    /// <summary>
    /// True si la asociación está en uso en trámites activos y por tanto el borrado
    /// debe rechazarse con HTTP 409.
    /// </summary>
    Task<bool> IsInUseAsync(
        Guid requirementId,
        CancellationToken cancellationToken = default);
}
