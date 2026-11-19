namespace Flit.PersonaNatural.Api.Abuso;

/// <summary>
/// Límites contra el uso abusivo del portal público (HU #14036). El portal no pide cuenta, así que se protege con captcha en
/// los puntos de entrada y con topes por IP, por cédula y globales. Todo se ajusta por ambiente (sección <c>Abuso</c>).
/// </summary>
public sealed class AbusoOptions
{
    public const string SectionName = "Abuso";

    /// <summary>Solicitudes por minuto y por IP a los endpoints públicos.</summary>
    public int LimiteIpPorMinuto { get; init; } = 30;

    /// <summary>Consultas por hora sobre una misma cédula.</summary>
    public int LimiteCedulaPorHora { get; init; } = 10;

    /// <summary>Consultas por día de toda la empresa «Persona Natural»; al alcanzarlo, las consultas se pausan.</summary>
    public int TopeGlobalDiario { get; init; } = 2000;

    /// <summary>
    /// Toma la IP real de <c>X-Forwarded-For</c>. Solo se enciende detrás del gateway (red interna): expuesto directo,
    /// cualquiera podría falsear su IP y saltarse el límite.
    /// </summary>
    public bool ConfiarEnXForwardedFor { get; init; }

    /// <summary>Máximo de saltos de confianza que se recorren (nginx → Next → gateway/YARP → servicio); es el <c>ForwardLimit</c>.</summary>
    public int SaltosDeConfianza { get; init; } = 3;

    /// <summary>Tope global del cuerpo de una petición JSON, en bytes; los archivos (multipart: adjuntos y comprobante) tienen su propio tope.</summary>
    public int MaxCuerpoJsonBytes { get; init; } = 64 * 1024;
}

/// <summary>Proveedor de captcha por configuración (sección <c>Captcha</c>): cambiarlo no requiere tocar código.</summary>
public sealed class CaptchaOptions
{
    public const string SectionName = "Captcha";
    public const string Turnstile = "turnstile";
    public const string Recaptcha = "recaptcha";
    public const string Deshabilitado = "deshabilitado";

    public string Proveedor { get; init; } = Turnstile;

    public string? SecretKey { get; init; }

    /// <summary>Solo para apuntar a un proveedor propio o a un simulador; por defecto sale del nombre del proveedor.</summary>
    public string? UrlVerificacion { get; init; }

    /// <summary>
    /// Segundo seguro para el proveedor «deshabilitado»: los VPS corren como Development, así que el ambiente por sí solo no
    /// basta. Hay que pedirlo explícito (<c>Captcha__PermitirDeshabilitado=true</c>), solo en el equipo del desarrollador.
    /// </summary>
    public bool PermitirDeshabilitado { get; init; }

    public static string UrlPorDefecto(string proveedor) => (proveedor ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        Turnstile => "https://challenges.cloudflare.com/turnstile/v0/siteverify",
        Recaptcha => "https://www.google.com/recaptcha/api/siteverify",
        _ => throw new InvalidOperationException($"Proveedor de captcha desconocido: «{proveedor}». Use {Turnstile}, {Recaptcha} o {Deshabilitado}."),
    };

    /// <summary>
    /// Falla al arrancar si el captcha quedó mal configurado. «deshabilitado» exige Development Y PermitirDeshabilitado=true:
    /// los VPS corren como Development, así que solo el ambiente no basta y un olvido dejaría el portal abierto.
    /// </summary>
    public static void Validar(IConfiguration configuration, bool esDevelopment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var opciones = configuration.GetSection(SectionName).Get<CaptchaOptions>() ?? new CaptchaOptions();
        var proveedor = (opciones.Proveedor ?? string.Empty).Trim().ToLowerInvariant();
        if (proveedor == Deshabilitado)
        {
            if (!esDevelopment || !opciones.PermitirDeshabilitado)
                throw new InvalidOperationException(
                    "Captcha__Proveedor=deshabilitado solo se permite en Development y con Captcha__PermitirDeshabilitado=true explícito; use turnstile o recaptcha.");
            return;
        }

        _ = UrlPorDefecto(proveedor);
        if (string.IsNullOrWhiteSpace(opciones.SecretKey))
            throw new InvalidOperationException($"Falta Captcha__SecretKey para el proveedor «{proveedor}» (en el .env: PERSONANATURAL_CAPTCHA_SECRET).");
    }
}
