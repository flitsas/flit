namespace Flit.Api.Identity;

/// <summary>Constantes del servidor OIDC del hub (FLIT Suite A-05, HU #12990).</summary>
public static class OidcDefaults
{
    /// <summary>Esquema de la sesión del hub: la abre el login del hub y la lee <c>/connect/authorize</c>.</summary>
    public const string HubSessionScheme = "FlitHubSession";

    /// <summary>Cookie de la sesión del hub. <c>HttpOnly</c>, <c>SameSite=Lax</c> y sin <c>Domain</c> (contrato §8).</summary>
    public const string HubSessionCookie = "flit_hub";

    /// <summary>Scopes de servicio (contrato §3).</summary>
    public static readonly string[] ServiceScopes = ["platform.manifest", "platform.consultas", "platform.me.read"];

    // Claims propios de la sesión del hub.
    public const string TenantIdClaim = "tenant_id";
    public const string DomainClaim = "dom";
    public const string SuperAdminClaim = "is_super_admin";

    /// <summary>Autorizaciones OIDC abiertas desde esta sesión del hub (A-13): se revocan al cerrarla.</summary>
    public const string AuthorizationClaim = "oidc_authz";
}
