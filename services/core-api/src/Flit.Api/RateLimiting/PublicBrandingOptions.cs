namespace Flit.Api.RateLimiting;

/// <summary>
/// Configuración de <c>GET /api/v1/public/branding</c> y <c>GET /api/v1/public/branding/logos/{id}</c>
/// (HU #12418 AC2/AC3, sección <c>PublicBranding</c>). Los hosts de FLIT NO se repiten aquí: ya los
/// resuelve <c>DomainContextMiddleware</c> vía <c>Domains:Reserved</c> (hecho 18/delta) — el handler
/// solo ve <c>DomainKind.Flit</c> vs <c>Network</c>, así que una lista de hosts duplicada aquí nunca
/// se leería.
/// </summary>
public sealed class PublicBrandingOptions
{
    public const string SectionName = "PublicBranding";

    /// <summary>
    /// Relleno de tiempo mínimo (ms) de respuesta, POR SI la suite de paridad #12429 exige un tiempo
    /// equivalente más estricto que el que ya da compartir caché/camino entre positivo y negativo
    /// (AC2). <c>0</c> en dev — sin efecto salvo que se configure.
    /// </summary>
    public int MinResponseMs { get; set; }

    public RateLimitOptions RateLimit { get; set; } = new();

    public sealed class RateLimitOptions
    {
        /// <summary>Peticiones admitidas por ventana y partición (IP), AC3.</summary>
        public int PermitLimit { get; set; } = 60;

        /// <summary>Duración de la ventana fija.</summary>
        public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
    }
}
