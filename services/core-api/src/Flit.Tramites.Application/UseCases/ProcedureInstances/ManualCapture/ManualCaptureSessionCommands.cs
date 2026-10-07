using Flit.Tramites.Application.Identity;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances.ManualCapture;

/// <summary>Códigos de error de la captura manual pública (contrato en <c>docs/design/EPICA-13202-contrato-api.md</c> §2).</summary>
public static class ManualCaptureErrors
{
    /// <summary>Token vacío, desconocido, regenerado o de otro proveedor: 404 sin revelar si existió.</summary>
    public const string NotFound = "not_found";

    /// <summary>El enlace superó sus 24 h (<c>ExpiresAt &lt; now</c>): 410.</summary>
    public const string Expirada = ManualCaptureStateCodes.Expirada;

    /// <summary>Ya enviada, aprobada, rechazada o expirada, o trámite anulado/revocado: 409.</summary>
    public const string EstadoInvalido = ManualCaptureStateCodes.EstadoInvalido;

    /// <summary>Se intenta enviar sin consentimiento del ciclo actual: 409.</summary>
    public const string ConsentimientoRequerido = ManualCaptureStateCodes.ConsentimientoRequerido;

    /// <summary><c>accepted</c> distinto de <c>true</c>: 400.</summary>
    public const string ConsentimientoNoAceptado = "consentimiento_no_aceptado";

    /// <summary>La versión del texto no es la vigente (<see cref="ManualCaptureConsent.TextVersion"/>): 400.</summary>
    public const string VersionTextoInvalida = "version_texto_invalida";

    /// <summary>Falta la firma (la firma es obligatoria): 422.</summary>
    public const string FirmaRequerida = "firma_requerida";

    /// <summary>Falta rostro, anverso o reverso: 422.</summary>
    public const string ArchivoRequerido = "archivo_requerido";

    /// <summary>El contenido (magic bytes) no es un formato admitido para ese archivo: 415.</summary>
    public const string TipoNoSoportado = "tipo_no_soportado";

    /// <summary>El archivo supera el tamaño máximo: 413.</summary>
    public const string ArchivoDemasiadoGrande = "archivo_demasiado_grande";
}

/// <summary>
/// Datos que ve el cliente en la pantalla de captura (<c>ManualCaptureView</c>). Sin nada más del trámite.
/// <see cref="ProductName"/> es el nombre del tipo de trámite; null en una prevalidación standalone (no hay producto).
/// </summary>
public sealed record ManualCaptureViewDto(
    string FullName,
    string DocumentType,
    string DocumentNumber,
    string? ProductName,
    DateTimeOffset ExpiresAt,
    string ConsentTextVersion);

/// <summary>Resolución compartida del token público de la captura manual a una validación en sesión vigente.</summary>
internal static class ManualCaptureSessionResolver
{
    /// <summary>
    /// Busca por el hash SHA-256 del token (el crudo nunca se persiste ni se loguea) y evalúa la sesión.
    /// Token vacío, desconocido o de otro proveedor ⇒ <see cref="ManualCaptureErrors.NotFound"/> genérico.
    /// </summary>
    public static async Task<(ProcedureInstanceBiometricValidation? Validation, string? Error)> ResolveAsync(
        IProcedureInstanceRepository repo, string token, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            return (null, ManualCaptureErrors.NotFound);

        var v = await repo.GetBiometricByTokenHashAsync(BiometricToken.Hash(token), ct).ConfigureAwait(false);
        if (v is null)
            return (null, ManualCaptureErrors.NotFound);

        // Bug #13055 — trámite anulado o revocado: el enlace ya no admite nada y la validación conserva su estado.
        if (v.CongeladaPorTramite)
            return (null, ManualCaptureErrors.EstadoInvalido);

        return v.EstadoSesionManual(now) switch
        {
            ManualCaptureSessionState.Vigente => (v, null),
            ManualCaptureSessionState.Vencida => (null, ManualCaptureErrors.Expirada),
            ManualCaptureSessionState.EstadoInvalido => (null, ManualCaptureErrors.EstadoInvalido),
            _ => (null, ManualCaptureErrors.NotFound),
        };
    }
}

/// <summary>
/// HU #13289 (Feature #13281 B, Épica #13202) — <c>GET /public/manual-capture/{token}</c>: datos para la pantalla.
/// Solo si la validación es del proveedor manual y está en <c>manual_activo</c>. Errores: <c>not_found</c> (404),
/// <c>expirada</c> (410), <c>estado_invalido</c> (409). Solo lectura: no persiste nada (a diferencia del magic-link mock).
/// </summary>
public sealed class GetManualCaptureHandler(IProcedureInstanceRepository repo, TimeProvider? clock = null)
{
    public async Task<(ManualCaptureViewDto? Result, string? Error)> HandleAsync(string token, CancellationToken ct = default)
    {
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        var (v, error) = await ManualCaptureSessionResolver.ResolveAsync(repo, token, now, ct).ConfigureAwait(false);
        if (v is null)
            return (null, error);

        return (new ManualCaptureViewDto(
            v.Name, v.DocumentType, v.DocumentNumber, v.ProcedureInstance?.ProcedureType?.Name, v.ExpiresAt,
            ManualCaptureConsent.TextVersion), null);
    }
}

/// <summary>Orden de registrar el consentimiento. <see cref="ClientIp"/> la resuelve el servidor, nunca el cuerpo.</summary>
public sealed record RegistrarConsentimientoManualCommand(string Token, bool Accepted, string? TextVersion, string? ClientIp);

/// <summary>
/// HU #13289 — <c>POST /public/manual-capture/{token}/consent</c>: guarda fecha/hora (servidor), IP y versión del texto
/// SOBRESCRIBIENDO los de un ciclo manual previo (la reactivación no los limpia) y audita <c>manual_consentimiento</c> sin
/// PII ni IP en el mensaje. Errores: los de la sesión más <c>consentimiento_no_aceptado</c> y <c>version_texto_invalida</c> (400).
/// </summary>
public sealed class RegistrarConsentimientoManualHandler(
    IProcedureInstanceRepository repo,
    IIdentityValidationAuditLog audit,
    TimeProvider? clock = null)
{
    public async Task<string?> HandleAsync(RegistrarConsentimientoManualCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = (clock ?? TimeProvider.System).GetUtcNow();
        var (v, error) = await ManualCaptureSessionResolver.ResolveAsync(repo, command.Token, now, ct).ConfigureAwait(false);
        if (v is null)
            return error;

        // El 400 va después de resolver la sesión: un token inexistente sigue siendo 404 sin importar el cuerpo.
        if (!command.Accepted)
            return ManualCaptureErrors.ConsentimientoNoAceptado;
        if (!string.Equals(command.TextVersion, ManualCaptureConsent.TextVersion, StringComparison.Ordinal))
            return ManualCaptureErrors.VersionTextoInvalida;

        v.RegistrarConsentimientoManual(ManualCaptureConsent.TextVersion, command.ClientIp, now);
        await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        await audit.LogAsync(new IdentityValidationAuditEntry(
            IdentityValidationAuditStages.ManualConsentimiento, IdentityValidationAuditOutcomes.Ok,
            TenantId: v.TenantId, ProcedureInstanceId: v.ProcedureInstanceId, ValidationId: v.Id, PartyRole: v.PartyRole,
            Message: "La persona aceptó el consentimiento biométrico de la captura manual.",
            Detail: $"version_texto={ManualCaptureConsent.TextVersion}"), ct).ConfigureAwait(false);

        return null;
    }
}
