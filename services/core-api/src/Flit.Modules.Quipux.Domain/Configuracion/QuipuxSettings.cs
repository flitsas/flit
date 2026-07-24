namespace Flit.Modules.Quipux.Domain.Configuracion;

/// <summary>
/// Configuración operativa de la integración Quipux (<c>admin.quipux_settings</c>, fila única).
/// Vive en BD y no en <c>appsettings</c>/env vars por requisito explícito: cambiar credenciales,
/// URLs o cadencia debe ser un UPDATE, sin desplegar. Los secretos se persisten cifrados con
/// Data Protection (keyring en Postgres) y esta clase expone ya el valor en claro — el descifrado
/// ocurre en el repositorio de Infrastructure.
/// </summary>
public sealed class QuipuxSettings
{
    public Guid Id { get; set; }

    /// <summary>
    /// Interruptor maestro. En false (o sin fila) los workers no hacen nada y la integración es
    /// inerte: es el modo de regresión. No se usa <c>IsDevelopment()</c> para esto porque el
    /// compose de PDN corre con <c>ASPNETCORE_ENVIRONMENT=Development</c>.
    /// </summary>
    public bool Enabled { get; set; }

    // --- Quipux API ---

    public string UrlLogin { get; set; } = string.Empty;

    public string UrlRegisterDocument { get; set; } = string.Empty;

    public string UrlValidateStatus { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    /// <summary>Descifrada. En BD se guarda como <c>password_enc</c>.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Código de consumidor asignado por Quipux a FLIT (1003). Viaja como <c>consumidor</c> en el cable del login y de cada payload.</summary>
    public string ConsumerCode { get; set; } = string.Empty;

    // --- Bucket S3 de Quipux ---
    // El bucket es de Quipux (qxinterconnect), no de FLIT: Quipux lee el PDF de ahí, y por eso
    // `contenidoDocumento` es la key S3 y no el PDF en base64. Es la única parte de la integración
    // donde FLIT maneja credenciales AWS directas (el File Manager corporativo no aplica aquí).

    public string Bucket { get; set; } = string.Empty;

    /// <summary>Prefijo de la key. En 1.0 está fijo como <c>FLIT/</c> → key final <c>FLIT/{documento}.pdf</c>.</summary>
    public string S3Prefix { get; set; } = "FLIT/";

    public string AwsRegion { get; set; } = "us-east-1";

    public string AwsAccessKeyId { get; set; } = string.Empty;

    /// <summary>Descifrada. En BD se guarda como <c>aws_secret_access_key_enc</c>.</summary>
    public string AwsSecretAccessKey { get; set; } = string.Empty;

    // --- Datos del funcionario que radica ---
    // En 1.0 iban hardcodeados en el servicio (tipo 3 = NIT, nro = NIT de FLIT).

    public int OfficerDocumentType { get; set; } = 3;

    public string OfficerDocumentNumber { get; set; } = string.Empty;

    // --- Cadencia y límites (configurables sin desplegar) ---

    /// <summary>Cada cuánto corre el registrador. Default 15 min (la cadencia real de FLIT 1.0, 7 días/semana).</summary>
    public int RegisterIntervalMinutes { get; set; } = 15;

    /// <summary>Cada cuánto corre el consultor de estado. Default 15 min.</summary>
    public int PollIntervalMinutes { get; set; } = 15;

    /// <summary>Máximo de submissions por ciclo. Sustituye al "límite de 20" de 1.0, que además estaba roto.</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>Intentos de radicación antes de pasar a <c>fallido</c> (dead-letter observable).</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Tope de consultas de estado por submission; acota el sondeo de un trámite que Quipux nunca resuelve.</summary>
    public int MaxPolls { get; set; } = 500;

    /// <summary>Timeout de las llamadas HTTP a Quipux. En 1.0 son 60 s compartidos entre registro y consulta.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    /// <summary>
    /// ¿La configuración alcanza para operar? Evita que el worker intente radicar con una fila a
    /// medio llenar y produzca errores contra Quipux en vez de un no-op silencioso.
    /// </summary>
    public bool EstaCompleta() =>
        Enabled
        && !string.IsNullOrWhiteSpace(UrlLogin)
        && !string.IsNullOrWhiteSpace(UrlRegisterDocument)
        && !string.IsNullOrWhiteSpace(UrlValidateStatus)
        && !string.IsNullOrWhiteSpace(Username)
        && !string.IsNullOrWhiteSpace(Password)
        && !string.IsNullOrWhiteSpace(ConsumerCode)
        && !string.IsNullOrWhiteSpace(Bucket)
        && !string.IsNullOrWhiteSpace(AwsAccessKeyId)
        && !string.IsNullOrWhiteSpace(AwsSecretAccessKey)
        && !string.IsNullOrWhiteSpace(OfficerDocumentNumber);
}
