using Flit.Admin.Domain.Integrations;

namespace Flit.Api.Authorization;

/// <summary>
/// HU #13087 (Épica #12737, ADR-0067) — esquema y policies de los clientes de integración externos
/// (<c>/api/v1/external/*</c>). El esquema valida SOLO pases con el emisor, la audiencia y la llave
/// externos; el esquema de la plataforma rechaza esos pases (ver <see cref="ApiSecurityExtensions"/>).
/// </summary>
public static class ExternalClientAuthorization
{
    public const string Scheme = "ExternalClient";

    /// <summary>Lectura del feed de trámites (<see cref="ExternalScopes.TramitesRead"/>).</summary>
    public const string TramitesReadPolicy = "ExternalTramitesRead";

    /// <summary>Datos personales sin enmascarar (<see cref="ExternalScopes.TramitesPiiRead"/>).</summary>
    public const string TramitesPiiReadPolicy = "ExternalTramitesPiiRead";

    /// <summary>Envío de adjuntos (<see cref="ExternalScopes.AttachmentsWrite"/>, HU #13263).</summary>
    public const string AttachmentsWritePolicy = "ExternalTramitesAttachmentsWrite";

    public const string ScopeClaim = "scope";

    /// <summary>Emisor por defecto del pase externo (<c>ExternalJwt:Issuer</c>).</summary>
    public const string DefaultIssuer = TokenValidationExtensions.DefaultExternalIssuer;

    /// <summary>Prefijo de las rutas externas.</summary>
    public const string RoutePrefix = "/api/v1/external";
}
