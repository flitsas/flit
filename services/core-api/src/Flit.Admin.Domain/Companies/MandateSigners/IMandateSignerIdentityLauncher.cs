namespace Flit.Admin.Domain.Companies.MandateSigners;

/// <summary>Desenlace de lanzar la validación de identidad propia de un mandatario.</summary>
public enum MandateSignerIdentityLaunchOutcome
{
    /// <summary>La validación se creó y el proveedor envió el enlace de captura al correo del mandatario.</summary>
    Sent,

    /// <summary>El proveedor falló de forma transitoria: la validación quedó encolada y se reintenta sola.</summary>
    Queued,

    /// <summary>Otro lanzamiento simultáneo ya dejó una validación activa para el mandatario (no se duplica).</summary>
    AlreadyInFlight,

    /// <summary>El proveedor rechazó el envío de forma definitiva o faltan datos: no hay validación nueva.</summary>
    Failed,
}

/// <param name="MandateSignerId">Ficha dueña de la validación (referencia exclusiva).</param>
/// <param name="TenantId">Tenant de la COMPAÑÍA del mandatario (HU #13121), nunca el del organismo.</param>
/// <param name="DocumentNumber">PII (Ley 1581): no se loguea.</param>
public sealed record MandateSignerIdentityLaunchRequest(
    Guid MandateSignerId,
    Guid TenantId,
    string DocumentType,
    string DocumentNumber,
    string FullName,
    string Email);

public sealed record MandateSignerIdentityLaunchResult(
    MandateSignerIdentityLaunchOutcome Outcome,
    Guid? ValidationId = null,
    string? Error = null);

/// <summary>
/// HU #13246 (Feature #13245, Épica #13090) — puerto con el que el módulo de mandatarios lanza la validación de identidad
/// PROPIA de un mandatario (party_role <c>mandatario</c> + referencia a la ficha) por el mismo flujo del trámite. La
/// implementación vive en Infrastructure y reutiliza el handler de prevalidación de Tramites; Admin no conoce al proveedor.
/// Cierra la validación anterior en vuelo del mismo mandatario antes de crear la nueva.
/// </summary>
public interface IMandateSignerIdentityLauncher
{
    Task<MandateSignerIdentityLaunchResult> LaunchAsync(
        MandateSignerIdentityLaunchRequest request,
        CancellationToken cancellationToken = default);
}
