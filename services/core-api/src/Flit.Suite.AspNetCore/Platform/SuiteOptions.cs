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
}

/// <summary>Bandera <c>Suite:ProductAccess:Enforce</c> del contrato §9. HU #12966 (B-06).</summary>
public sealed class ProductAccessOptions
{
    public const string SectionName = "Suite:ProductAccess";

    /// <summary>Encendida, <c>RequireProduct</c> rechaza con 403; apagada (por defecto), solo registra.</summary>
    public bool Enforce { get; set; }
}
