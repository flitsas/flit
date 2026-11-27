namespace Flit.Impuestos.Api.Modulos.ApiLiquidacion;

/// <summary>
/// Configuración del cliente del API de liquidación (plan §5.6), sección <see cref="Seccion"/>:
/// <list type="bullet">
///   <item><c>Impuestos__Api__BaseUrl</c> ← <c>IMPUESTOS_SVC_API_BASE_URL</c>: base del API, https (http solo en loopback, para el stub).</item>
///   <item><c>Impuestos__Api__IntegrationToken</c> ← <c>IMPUESTOS_SVC_INTEGRATION_TOKEN</c>: secreto de ambiente, solo para el alta (plan §4.2).</item>
///   <item><c>Impuestos__Api__TimeoutSegundos</c> ← <c>IMPUESTOS_SVC_HTTP_TIMEOUT_SECONDS</c>: timeout de cada llamada (30 s por defecto).</item>
/// </list>
/// Sin valores aquí el servicio arranca, pero cada llamada falla cerrada con un mensaje que dice qué variable falta (la
/// obligatoriedad al arrancar la suma M07 en <c>ServicioSettings</c>). <see cref="ToString"/> nunca muestra el token.
/// </summary>
/// <example>
/// <code>
/// services.Configure&lt;OpcionesApiLiquidacion&gt;(configuration.GetSection(OpcionesApiLiquidacion.Seccion));
/// </code>
/// </example>
public sealed class OpcionesApiLiquidacion
{
    public const string Seccion = "Impuestos:Api";
    public const int TimeoutPorDefectoSegundos = 30;

    public string? BaseUrl { get; set; }

    public string? IntegrationToken { get; set; }

    public int TimeoutSegundos { get; set; } = TimeoutPorDefectoSegundos;

    /// <summary>Timeout efectivo: el configurado si es positivo; si no, <see cref="TimeoutPorDefectoSegundos"/>.</summary>
    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSegundos > 0 ? TimeoutSegundos : TimeoutPorDefectoSegundos);

    /// <summary>La base validada (absoluta, https o http en loopback, sin barra final); si no, no se llama al API.</summary>
    public Uri BaseValidada()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl) || !Uri.TryCreate(BaseUrl.Trim().TrimEnd('/'), UriKind.Absolute, out var base_))
            throw SinConfigurar("BaseUrl", "IMPUESTOS_SVC_API_BASE_URL", "falta o no es una URL absoluta");
        if (base_.Scheme != Uri.UriSchemeHttps && !(base_.Scheme == Uri.UriSchemeHttp && base_.IsLoopback))
            throw SinConfigurar("BaseUrl", "IMPUESTOS_SVC_API_BASE_URL", "debe ser https (http solo en loopback, para el stub local)");
        return base_;
    }

    /// <summary>El token de integración; si falta, el alta no se intenta.</summary>
    public string TokenValidado() =>
        string.IsNullOrWhiteSpace(IntegrationToken)
            ? throw SinConfigurar("IntegrationToken", "IMPUESTOS_SVC_INTEGRATION_TOKEN", "falta")
            : IntegrationToken;

    public override string ToString() =>
        $"OpcionesApiLiquidacion {{ BaseUrl = {BaseUrl}, IntegrationToken = {(string.IsNullOrEmpty(IntegrationToken) ? "(vacío)" : "[REDACTADO]")}, TimeoutSegundos = {TimeoutSegundos} }}";

    private static InvalidOperationException SinConfigurar(string clave, string variableEnv, string motivo) => new(
        $"El API de liquidación no está configurado: Impuestos__Api__{clave} {motivo} (en el .env: {variableEnv}).");
}
