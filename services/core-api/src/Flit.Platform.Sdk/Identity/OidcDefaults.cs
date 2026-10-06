namespace Flit.Api.Identity;

/// <summary>Constantes del servidor OIDC del hub (FLIT Suite A-05, HU #12990).</summary>
public static class OidcDefaults
{
    /// <summary>Esquema de la sesión del hub: la abre el login del hub y la lee <c>/connect/authorize</c>.</summary>
    public const string HubSessionScheme = "FlitHubSession";

    /// <summary>Cookie de la sesión del hub. <c>HttpOnly</c>, <c>SameSite=Lax</c> y sin <c>Domain</c> (contrato §8).</summary>
    public const string HubSessionCookie = "flit_hub";

    /// <summary>
    /// Scopes de servicio (contrato §3) y el servicio que atiende cada uno, que es el <c>aud</c> del token (contrato v1.3,
    /// Epic #13316, HU #13333). Un token con scopes de varios servicios lleva todas sus audiencias.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ServiceScopeAudiences = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["platform.manifest"] = ServiceAudiences.Plataforma,
        ["platform.me.read"] = ServiceAudiences.Plataforma,
        ["platform.identidad.read"] = ServiceAudiences.Plataforma,
        ["platform.consultas"] = ServiceAudiences.Consultas,
        ["platform.notificaciones.send"] = ServiceAudiences.Notificaciones,
        ["platform.tramites.ict"] = ServiceAudiences.Tramites,
    };

    /// <summary>Prefijo de los clientes de servicio (contrato §3, <c>svc-&lt;código&gt;</c>): su <c>sub</c> en el token.</summary>
    public const string ServiceClientPrefix = "svc-";

    /// <summary>Scopes de servicio (contrato §3).</summary>
    public static readonly string[] ServiceScopes = [.. ServiceScopeAudiences.Keys];

    /// <summary>
    /// Audiencias de un token de servicio con estos scopes, sin repetir y en orden. Un scope que no es de servicio no
    /// aporta audiencia; si ninguno la aporta, <c>plataforma</c> (el comportamiento de antes del contrato v1.3).
    /// </summary>
    public static IReadOnlyList<string> AudiencesForServiceScopes(IEnumerable<string> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        var audiences = scopes
            .Select(s => ServiceScopeAudiences.TryGetValue(s, out var aud) ? aud : null)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        return audiences.Count > 0 ? audiences : [ServiceAudiences.Plataforma];
    }

    // Claims propios de la sesión del hub.
    public const string TenantIdClaim = "tenant_id";
    public const string DomainClaim = "dom";
    public const string SuperAdminClaim = "is_super_admin";

    /// <summary>Autorizaciones OIDC abiertas desde esta sesión del hub (A-13): se revocan al cerrarla.</summary>
    public const string AuthorizationClaim = "oidc_authz";

    /// <summary>
    /// Origen de cada producto que abrió sesión desde esta sesión del hub (sale de su <c>redirect_uri</c> validado). Al
    /// cerrar sesión se le pide a cada uno que borre su cookie (front-channel logout, HU #13004).
    /// </summary>
    public const string RelyingPartyClaim = "oidc_rp";
}

/// <summary>
/// Audiencia (<c>aud</c>) de los tokens de servicio: el código del servicio que recibe la llamada (contrato v1.3 §3).
/// <c>plataforma</c> es Identidad, que también atiende los endpoints de plataforma.
/// </summary>
public static class ServiceAudiences
{
    public const string Plataforma = "plataforma";
    public const string Consultas = "consultas";
    public const string Notificaciones = "notificaciones";
    public const string Tramites = "tramites";
}
