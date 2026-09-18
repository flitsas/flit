namespace Flit.Admin.Application.Companies.Domains.Verification;

/// <summary>
/// Configuración del ciclo de comprobación DNS (HU #12425, sección <c>Domains:Verification</c>).
/// Infraestructura la liga con <c>IOptions&lt;DomainVerificationOptions&gt;</c> y expone el POCO
/// resuelto (mismo patrón que <c>DomainOptions</c> / <c>ImprontaValidationPolicyOptions</c>) para que
/// <see cref="VerifyDomainHandler"/> y el <c>BackgroundService</c> no dependan de
/// <c>Microsoft.Extensions.Options</c>.
/// </summary>
public sealed class DomainVerificationOptions
{
    public const string SectionName = "Domains:Verification";

    /// <summary>Interruptor del job programado (AC6: apagado o sin dominios ⇒ no hace nada).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Cada cuánto el <c>BackgroundService</c> sondea <c>next_check_at</c>.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Primer reintento tras un fallo (<c>pending</c>/<c>failed</c>).</summary>
    public TimeSpan InitialRetryInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Tope del backoff exponencial de reintentos.</summary>
    public TimeSpan MaxRetryInterval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>Factor multiplicador del backoff exponencial por intento consecutivo fallido.</summary>
    public double BackoffFactor { get; set; } = 2.0;

    /// <summary>Revalidación periódica de un dominio ya <c>active</c> (AC4).</summary>
    public TimeSpan RevalidationInterval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Periodo de gracia cuando el TXT desaparece en un dominio <c>active</c> (AC4).</summary>
    public TimeSpan GracePeriod { get; set; } = TimeSpan.FromDays(3);

    /// <summary>Enfriamiento mínimo entre dos comprobaciones a demanda del mismo dominio (<c>POST .../domain/verify</c>).</summary>
    public int ManualCooldownSeconds { get; set; } = 30;

    /// <summary>Timeout de la consulta DNS (auditoría DnsClient, condición 2).</summary>
    public TimeSpan DnsTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Servidores DNS a usar (IP o host); vacío ⇒ resolutor del sistema (default). Configurable por
    /// ambiente, nunca hardcodeado en código (auditoría DnsClient, condición 2).
    /// </summary>
    public IReadOnlyList<string> DnsServers { get; set; } = [];

    /// <summary>Tamaño de lote del claim del <c>BackgroundService</c> por ciclo.</summary>
    public int BatchSize { get; set; } = 25;
}
