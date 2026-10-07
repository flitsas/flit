namespace Flit.Tramites.Domain.Identity;

/// <summary>
/// HU #13289/#13290 (Feature #13281 B, Épica #13202) — la validación manual no admite la operación pedida
/// (consentimiento o captura) en su estado actual. <see cref="Code"/> es uno de <see cref="ManualCaptureStateCodes"/>.
/// Los casos de uso comprueban antes y responden con el código; la excepción es la red de seguridad de la entidad.
/// Sin PII en el mensaje.
/// </summary>
public sealed class ManualCaptureStateException(string code)
    : InvalidOperationException($"La captura manual no admite la operación ({code}).")
{
    public string Code { get; } = code;
}

/// <summary>Códigos de la red de seguridad de la entidad (coinciden con los códigos de error del contrato público).</summary>
public static class ManualCaptureStateCodes
{
    public const string EstadoInvalido = "estado_invalido";
    public const string Expirada = "expirada";
    public const string ConsentimientoRequerido = "consentimiento_requerido";
}

/// <summary>Estado de la sesión de captura de un enlace manual en un instante dado.</summary>
public enum ManualCaptureSessionState
{
    /// <summary>La fila no es del flujo manual (otro proveedor): el enlace no existe para este flujo.</summary>
    NoManual,

    /// <summary>Flujo manual, en espera de captura y dentro de las 24 h.</summary>
    Vigente,

    /// <summary>Flujo manual en espera de captura pero pasado <c>ExpiresAt</c>.</summary>
    Vencida,

    /// <summary>Flujo manual ya enviado, aprobado, rechazado o expirado: el enlace ya no admite captura.</summary>
    EstadoInvalido,
}

/// <summary>
/// Versión VIGENTE del texto de consentimiento biométrico de la captura manual. Única fuente: el front la lee en
/// <c>consentTextVersion</c> de la vista y la devuelve al aceptar, y la constancia guarda qué versión aceptó cada persona.
/// Súbase al cambiar el texto (el literal de Kyverum lo entrega el PO; ver dudas abiertas de la Épica #13202).
/// </summary>
public static class ManualCaptureConsent
{
    public const string TextVersion = "manual-ley1581-v1";
}
