namespace Flit.Admin.Domain.Companies.MandateSigners;

/// <summary>Desenlace de consultar al proveedor el estado de la validación propia de un mandatario.</summary>
public enum MandateSignerIdentityReconcileOutcome
{
    /// <summary>Se consultó (o no hacía falta): <see cref="MandateSignerIdentityReconcileResult.Status"/> es el estado vigente.</summary>
    Ok,

    /// <summary>La ficha no tiene ninguna validación propia.</summary>
    SinValidacion,

    /// <summary>El proveedor no respondió; el worker lo sigue intentando.</summary>
    ProveedorNoDisponible,
}

/// <param name="Status">Estado de la validación más reciente del mandatario tras la consulta (vocabulario del módulo Identidad).</param>
/// <param name="Updated">La consulta cambió el estado (p. ej. el webhook se perdió y la aprobación llegó por aquí).</param>
public sealed record MandateSignerIdentityReconcileResult(
    MandateSignerIdentityReconcileOutcome Outcome,
    string? Status = null,
    bool Updated = false);

/// <summary>
/// Consulta al proveedor el estado real de la validación propia del mandatario y lo aplica, igual que la pantalla de
/// espera del trámite. Es el respaldo cuando el webhook no llega: la ficha lo invoca al abrirse y desde «Consultar
/// estado». La implementación vive en Infrastructure y reutiliza la reconciliación de Tramites.
/// </summary>
public interface IMandateSignerIdentityReconciler
{
    Task<MandateSignerIdentityReconcileResult> ReconcileAsync(
        Guid mandateSignerId,
        CancellationToken cancellationToken = default);
}
