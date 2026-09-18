namespace Flit.Modules.Security.Domain.Auth;

/// <summary>
/// Catálogo cerrado de la clase de tema aplicado a un correo (HU #12428 AC1/AC4). Ningún otro
/// valor es válido — cualquier resolución que no pueda demostrar una marca publicada de una cabeza
/// MARCA_BLANCA activa (propia o de su padre) resuelve <see cref="Flit"/>, nunca un tercer valor.
/// </summary>
public enum EmailThemeKind
{
    /// <summary>Plantilla de respaldo FLIT — la que existía antes de la Épica Marca Blanca.</summary>
    Flit,

    /// <summary>Marca publicada de una cabeza MARCA_BLANCA (propia, o heredada por una hija).</summary>
    Brand,
}

/// <summary>
/// Tema de correo ya resuelto, listo para aplicar sobre la estructura fija de cualquier plantilla
/// del catálogo (HU #12428 AC1/AC2/AC6). Solo contiene VALORES — nombre, URL de logotipo y tres
/// colores hex — nunca marcado ni texto libre: es la única superficie que
/// <see cref="Flit.Modules.Security.Domain.Auth.BrandedEmailChrome"/> puede interpolar sin volver a
/// validar el origen del dato.
/// </summary>
/// <param name="Kind">Clase del tema (AC1). Determina si el chrome de marca se aplica.</param>
/// <param name="PlatformName">Nombre visible en encabezado y pie (AC2). Se escapa siempre con
/// <c>HtmlEncode</c> antes de interpolar — ver <see cref="BrandedEmailChrome"/> (AC6).</param>
/// <param name="LogoUrl">URL pública ABSOLUTA y versionada del logotipo (AC3). <c>null</c> con
/// <see cref="EmailThemeKind.Flit"/> (esa variante usa sus propios assets, sin cambios).</param>
/// <param name="Primary">Color principal hex <c>#RRGGBB</c> (AC2) — botones y enlaces.</param>
/// <param name="Secondary">Color secundario hex <c>#RRGGBB</c> — reservado para variantes futuras del
/// chrome; hoy no se usa en la interpolación visual.</param>
/// <param name="OnPrimary">Color de contraste sobre <see cref="Primary"/> hex <c>#RRGGBB</c>.</param>
/// <param name="Version"><c>tenant_brandings.published_version</c> de la marca aplicada (AC5).
/// <c>0</c> con <see cref="EmailThemeKind.Flit"/> — nunca se persiste como versión real.</param>
public sealed record EmailTheme(
    EmailThemeKind Kind,
    string PlatformName,
    string? LogoUrl,
    string Primary,
    string Secondary,
    string OnPrimary,
    int Version)
{
    /// <summary>
    /// Tema de respaldo (AC4/AC9): mismos valores que <c>BrandIdentity.Flit</c>
    /// (<c>Flit.Admin.Domain.Companies.Branding</c>) para que ambos catálogos de "identidad FLIT"
    /// nunca diverjan, sin que este proyecto (Domain, sin dependencias de Admin) tenga que
    /// referenciarlo.
    /// </summary>
    public static readonly EmailTheme Flit = new(
        Kind: EmailThemeKind.Flit,
        PlatformName: "FLIT 2.0",
        LogoUrl: null,
        Primary: "#162744",
        Secondary: "#557EFF",
        OnPrimary: "#FFFFFF",
        Version: 0);

    public bool IsBrand => Kind == EmailThemeKind.Brand;

    /// <summary>Vocabulario estable persistido en <c>admin.notification_delivery_logs.theme_kind</c>
    /// (HU #12428 AC5, DDL 117: <c>flit</c> | <c>brand</c>) y expuesto en el contrato de muestra
    /// (HU #12431).</summary>
    public string KindWireValue => Kind == EmailThemeKind.Brand ? "brand" : "flit";
}
