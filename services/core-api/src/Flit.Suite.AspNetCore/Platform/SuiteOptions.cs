namespace Flit.Api.Platform;

/// <summary>
/// Hosts de la suite (contrato de plataforma v1, §1 y §11), sección <c>Suite:Hosts</c>. HU #12966 (B-06).
/// </summary>
public sealed class SuiteHostsOptions
{
    public const string SectionName = "Suite:Hosts";

    /// <summary>Dominio raíz: <c>flitsas.online</c>.</summary>
    public string Root { get; set; } = "flitsas.online";

    /// <summary>Prefijo del ambiente: vacío en PDN, <c>qa</c> o <c>dev</c>.</summary>
    public string Environment { get; set; } = string.Empty;

    public string Scheme { get; set; } = "https";

    /// <summary>
    /// URL fija por producto, para desarrollo local (§11): por ejemplo <c>tramites</c> →
    /// <c>http://localhost:3000</c>. Si un producto tiene reemplazo, se usa tal cual.
    /// </summary>
    public Dictionary<string, string> Overrides { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Productos del catálogo que todavía no tienen app desplegada en este ambiente (Comparendos y Diagnóstico al abrir
    /// la suite): su enlace lleva a la pantalla «Próximamente» del hub (<c>/proximamente/&lt;código&gt;</c>) en vez de a
    /// un host que no responde. Al desplegar el producto en un ambiente, se quita de la lista de ese ambiente.
    /// </summary>
    public List<string> ComingSoon { get; set; } = [];

    /// <summary>
    /// Raíces alternativas del ambiente, servidas a la vez que la principal (PDN: el hub en <c>app.flitsas.com</c> y
    /// cada producto en <c>&lt;producto&gt;.flitsas.com</c>, además de <c>flitsas.online</c>). Quien entra por una
    /// raíz se queda en ella: emisor OIDC, retornos del login, enlaces del menú y correos salen del host de la
    /// petición. Lista cerrada: un host que no esté aquí nunca se usa como emisor. Vacía en DEV y QA.
    /// </summary>
    public List<SuiteAlternateRoot> AlternateRoots { get; set; } = [];
}

/// <summary>Una raíz alternativa de <see cref="SuiteHostsOptions.AlternateRoots"/>.</summary>
public sealed class SuiteAlternateRoot
{
    /// <summary>Host del hub en esta raíz: <c>app.flitsas.com</c>.</summary>
    public string Hub { get; set; } = string.Empty;

    /// <summary>Host de cada producto, con <c>{product}</c> en lugar del código: <c>{product}.flitsas.com</c>.</summary>
    public string Products { get; set; } = string.Empty;
}

/// <summary>Bandera <c>Suite:ProductAccess:Enforce</c> del contrato §9. HU #12966 (B-06).</summary>
public sealed class ProductAccessOptions
{
    public const string SectionName = "Suite:ProductAccess";

    /// <summary>Encendida, <c>RequireProduct</c> rechaza con 403; apagada (por defecto), solo registra.</summary>
    public bool Enforce { get; set; }
}
