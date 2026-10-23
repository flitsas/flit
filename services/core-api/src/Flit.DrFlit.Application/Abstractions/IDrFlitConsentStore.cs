namespace Flit.DrFlit.Application.Abstractions;

/// <summary>
/// Aceptación del tratamiento de datos de DR. FLIT (HU #12931). El chat con IA y los casos de soporte
/// solo se usan con la versión vigente aceptada; el menú sin IA no la necesita.
/// </summary>
public interface IDrFlitConsentStore
{
    Task<bool> HasAcceptedAsync(Guid userId, string version, CancellationToken ct);

    /// <summary>Registra la aceptación. Idempotente: aceptar dos veces la misma versión no duplica.</summary>
    Task RecordAsync(DrFlitConsentAcceptance acceptance, CancellationToken ct);
}

/// <summary>Evidencia de la aceptación.</summary>
public sealed record DrFlitConsentAcceptance(
    Guid TenantId,
    Guid UserId,
    string Version,
    string? ClientIp,
    string? UserAgent);

/// <summary>Versión vigente del texto (<c>DrFlit:Consent:Version</c>). Cambiarla vuelve a pedirla a todos.</summary>
public interface IDrFlitConsentSettings
{
    string CurrentVersion { get; }
}
