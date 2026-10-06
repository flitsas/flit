namespace Flit.Infrastructure.Consultations;

/// <summary>
/// FEATURE 05 — configuración del API de registro de FLIT (heredado de FLIT 1), que sirve la
/// fuente INTERNA de comparendos.
///
/// ⚠️ <see cref="BaseUrl"/> incluye un segmento de ruta (<c>/pdn</c>, el stage del API Gateway).
/// Al componer la URL hay que conservar la barra final o <c>new Uri(base, relative)</c> descarta
/// ese segmento y la llamada se va a la raíz. Lo resuelve <see cref="NormalizedBaseUrl"/>.
///
/// Sin credenciales: el API no exige cabecera de autorización.
/// </summary>
public sealed class FlitRegistrationApiOptions
{
    public const string SectionName = "RegistrationApi";

    public string BaseUrl { get; set; } = "https://knli4dcix0.execute-api.us-east-1.amazonaws.com/pdn";

    /// <summary>Ruta RELATIVA (sin barra inicial) del recurso de comparendos.</summary>
    public string InfractionPath { get; set; } = "api/v1/registration/simit";

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>BaseUrl con barra final garantizada, para no perder el segmento de stage.</summary>
    public string NormalizedBaseUrl =>
        BaseUrl.EndsWith('/') ? BaseUrl : BaseUrl + "/";

    /// <summary>InfractionPath sin barra inicial, para que resuelva relativo al stage y no a la raíz.</summary>
    public string NormalizedInfractionPath =>
        InfractionPath.TrimStart('/');
}
