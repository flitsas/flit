namespace Flit.Infrastructure.FlitoSignals;

/// <summary>
/// HU #13625 (Feature #13618, Épica #13614) — configuración por ambiente de los avisos de sincronización a FLITO.
/// Sección <c>Integrations:FlitoSignals</c>. Apagado por defecto: un ambiente sin la sección no envía nada, y PDN se
/// enciende solo después de acordarlo con FLITO (runbook de clientes FLITO §6).
/// La clave HMAC es una por ambiente para todos los tenants, se entrega a FLITO fuera de banda y llega por variable de
/// entorno: nunca va en appsettings, en el repositorio ni en un work item.
/// </summary>
public sealed class FlitoSignalsOptions
{
    public const string SectionName = "Integrations:FlitoSignals";

    /// <summary>Largo mínimo de la clave HMAC, en caracteres (32 bytes aleatorios en base64 dan 44).</summary>
    public const int MinSigningKeyLength = 32;

    /// <summary>Enciende el despachador. <c>false</c> por defecto.</summary>
    public bool Enabled { get; set; }

    /// <summary>URL absoluta HTTPS de recepción de FLITO, p. ej. <c>https://dev.operaciones.flitsas.online/api/flito/sync/flit2/avisos</c>.</summary>
    public string? TargetUrl { get; set; }

    /// <summary>Clave HMAC-SHA256 del ambiente. Secreto: no se registra en logs ni en mensajes de error.</summary>
    public string? SigningKey { get; set; }

    /// <summary>Cada cuánto revisa el despachador los avisos pendientes.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Antigüedad mínima de un cambio para avisarlo: la ventana de estabilidad del feed v3.1. Antes, FLITO leería el
    /// feed y todavía no encontraría el cambio.
    /// </summary>
    public TimeSpan StabilityLag { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Tope de avisos por ciclo.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// ¿Puede despachar? Devuelve el motivo cuando está encendido pero mal configurado. El motivo nunca incluye la
    /// clave: se puede registrar tal cual.
    /// </summary>
    public bool EsOperable(out string? motivo)
    {
        motivo = null;
        if (!Enabled)
        {
            return false;
        }

        if (!Uri.TryCreate(TargetUrl, UriKind.Absolute, out var destino) || destino.Scheme != Uri.UriSchemeHttps)
        {
            motivo = $"{SectionName}:TargetUrl debe ser una URL absoluta HTTPS.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(SigningKey) || SigningKey.Trim().Length < MinSigningKeyLength)
        {
            motivo = $"{SectionName}:SigningKey falta o tiene menos de {MinSigningKeyLength} caracteres.";
            return false;
        }

        // La clave se firma tal cual (bytes UTF-8, sin recortar): un espacio o salto de línea sobrante en el .env haría
        // fallar todas las firmas en FLITO, así que se rechaza aquí en vez de recortarla en silencio.
        if (SigningKey.Length != SigningKey.Trim().Length)
        {
            motivo = $"{SectionName}:SigningKey tiene espacios o saltos de línea al inicio o al final.";
            return false;
        }

        if (PollInterval <= TimeSpan.Zero || StabilityLag < TimeSpan.Zero || BatchSize < 1)
        {
            motivo = $"{SectionName}: PollInterval y BatchSize deben ser positivos y StabilityLag no puede ser negativo.";
            return false;
        }

        return true;
    }
}
