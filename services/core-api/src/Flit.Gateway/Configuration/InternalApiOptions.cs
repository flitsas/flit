namespace Flit.Gateway.Configuration;

/// <summary>
/// Acceso interno del Gateway a <c>/api/v1/internal/*</c> de <c>Flit.Api</c> (HU #12417 AC3,
/// ADR-0060 D2): el Gateway NO tiene BD propia, así que consume el endpoint DIRECTO (fuera de
/// YARP — la ruta pública se bloquea en el propio Gateway) para derivar los orígenes CORS
/// dinámicos. Clave compartida vía <c>Internal:ApiKey</c> en ambos servicios.
/// </summary>
public sealed class InternalApiOptions
{
    public const string SectionName = "Internal";

    /// <summary>Base URL del clúster core-api (mismo destino lógico que ReverseProxy:Clusters:core-api-cluster).</summary>
    public string ApiBaseUrl { get; set; } = "http://core-api:4003";

    /// <summary>
    /// Clave compartida (cabecera <c>X-Internal-Key</c>). Vacía por defecto: fail-closed — sin
    /// clave configurada, <see cref="Flit.Gateway.Cors.DynamicCorsOriginSource"/> nunca llama a la
    /// API interna y CORS queda limitado a la lista fija.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}
