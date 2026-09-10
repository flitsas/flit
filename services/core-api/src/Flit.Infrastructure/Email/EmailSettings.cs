namespace Flit.Infrastructure.Email;

/// <summary>Configuración SMTP (sección "Smtp" de appsettings).</summary>
public sealed class EmailSettings
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public bool UseStartTls { get; set; } = true;

    public bool DisableAuthentication { get; set; }

    public string DefaultSenderEmail { get; set; } = string.Empty;

    public string DefaultSenderPassword { get; set; } = string.Empty;

    public string DefaultSenderName { get; set; } = "FLIT Trámites";

    /// <summary>
    /// Controla <see cref="MailKit.MailService.CheckCertificateRevocation"/> del cliente SMTP.
    /// Por defecto <c>true</c>: se sigue comprobando que el certificado del servidor no haya sido
    /// <b>revocado</b> antes de su vencimiento (además de la cadena de confianza, el emisor, el
    /// nombre de host y la vigencia, que MailKit valida siempre y esta propiedad no afecta).
    /// Ponerlo en <c>false</c> NO desactiva la validación TLS del certificado — solo deja de
    /// consultarse el estado de revocación (CRL/OCSP). Es útil cuando esos servidores de
    /// revocación están caídos o son inalcanzables por la red del entorno (p. ej. detrás de un
    /// proxy o antivirus que inspecciona TLS), escenario documentado por MailKit para esta misma
    /// propiedad. El defecto es <c>true</c> a propósito: un despliegue que no defina esta clave
    /// debe seguir comprobando revocación.
    /// </summary>
    public bool CheckCertificateRevocation { get; set; } = true;
}
