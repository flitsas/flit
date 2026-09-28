namespace Flit.Api.Identity;

/// <summary>
/// Servidor OIDC del hub (FLIT Suite A-05, HU #12990), sección <c>Suite:Oidc</c>. Apagado por defecto (contrato §9):
/// sin la bandera no se registra nada y el login actual sigue igual.
/// </summary>
public sealed class OidcOptions
{
    public const string SectionName = "Suite:Oidc";

    /// <summary><c>Suite:Oidc:Enabled</c>. Enciende <c>/connect/*</c>, el descubrimiento y el JWKS.</summary>
    public bool Enabled { get; set; }

    /// <summary>Vida del access token: 15 minutos (contrato §2).</summary>
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>Vida del refresh token. Se rota en cada uso.</summary>
    public int RefreshTokenDays { get; set; } = 14;

    /// <summary>Vida de la sesión del hub (cookie <c>flit_hub</c>), con renovación por uso.</summary>
    public int HubSessionHours { get; set; } = 12;

    /// <summary>Pantalla de login del hub (A-06), en el mismo host del emisor.</summary>
    public string LoginPath { get; set; } = "/login";

    /// <summary>Ruta de retorno de cada producto tras el login; la sirve <c>@flit/auth</c> (A-09).</summary>
    public string CallbackPath { get; set; } = "/auth/callback";

    /// <summary><c>key_id</c> de las llaves en <c>security.jwt_signing_keys</c>. Rotar = cambiar el id.</summary>
    public string SigningKeyId { get; set; } = "flit-oidc-signing-v1";

    public string EncryptionKeyId { get; set; } = "flit-oidc-encryption-v1";

    /// <summary>
    /// Clientes de servicio (contrato §3), por <c>client_id</c> (<c>svc-&lt;código&gt;</c>). Sin secreto no se registran:
    /// el secreto solo va en el <c>.env</c> del servidor.
    /// </summary>
    public Dictionary<string, OidcServiceClientOptions> ServiceClients { get; set; } = new(StringComparer.Ordinal);

    /// <summary>URIs de retorno adicionales por producto (por ejemplo, <c>http://localhost:3000/auth/callback</c> en local).</summary>
    public Dictionary<string, List<string>> ExtraRedirectUris { get; set; } = new(StringComparer.Ordinal);
}

public sealed class OidcServiceClientOptions
{
    public string Secret { get; set; } = string.Empty;

    public List<string> Scopes { get; set; } = [];
}
