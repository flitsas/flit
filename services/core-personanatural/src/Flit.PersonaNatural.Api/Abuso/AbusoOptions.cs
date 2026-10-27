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

    public static string UrlPorDefecto(string proveedor) => (proveedor ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        Turnstile => "https://challenges.cloudflare.com/turnstile/v0/siteverify",
        Recaptcha => "https://www.google.com/recaptcha/api/siteverify",
        _ => throw new InvalidOperationException($"Proveedor de captcha desconocido: «{proveedor}». Use {Turnstile}, {Recaptcha} o {Deshabilitado}."),
    };

    /// <summary>
    /// Falla al arrancar si el captcha quedó mal configurado. «deshabilitado» solo se permite en Development: en cualquier
    /// otro ambiente un olvido dejaría el portal abierto sin que nadie se entere.
    /// </summary>
    public static void Validar(IConfiguration configuration, bool esDevelopment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var opciones = configuration.GetSection(SectionName).Get<CaptchaOptions>() ?? new CaptchaOptions();
        var proveedor = (opciones.Proveedor ?? string.Empty).Trim().ToLowerInvariant();
        if (proveedor == Deshabilitado)
        {
            if (!esDevelopment)
                throw new InvalidOperationException("Captcha__Proveedor=deshabilitado solo se permite en Development; use turnstile o recaptcha.");
            return;
        }

        _ = UrlPorDefecto(proveedor);
        if (string.IsNullOrWhiteSpace(opciones.SecretKey))
            throw new InvalidOperationException($"Falta Captcha__SecretKey para el proveedor «{proveedor}» (en el .env: PERSONANATURAL_CAPTCHA_SECRET).");
    }
}
