namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #12774 — tipos de adjunto del certificado de Cámara de Comercio, uno por rol.
/// <para>
/// El certificado acredita QUIÉN representa a una sociedad: pertenece a la familia de las
/// escrituras, no a la del baúl de firmas. Por eso se pide por actor y no por trámite, y por eso
/// cada rol tiene su propio código: el emparejamiento adjunto ↔ requisito es por tipo, así que con
/// un solo código el certificado del vendedor dejaría «satisfecho» también al comprador — que es
/// justo el caso de un traspaso entre dos sociedades.
/// </para>
/// <para>
/// <b>No existe un rol «propietario».</b> Los roles del modelo son <c>vendedor</c>,
/// <c>comprador</c> y <c>locatario</c> (<c>ParteRol</c> → entidades OWNER, BUYER, LESSEE); el
/// propietario se deriva de la modalidad — en traspaso es el vendedor y en matrícula inicial el
/// comprador — así que queda cubierto sin un código propio.
/// </para>
/// <para>
/// <b>No se reutiliza el código <c>camara_comercio</c></b> que ya existe en el catálogo: describe
/// otra cosa («Cámara de Comercio + cédula representante», sin rol) y arrastra los datos migrados
/// de V1, donde <c>V1AttachmentMap</c> colapsa en él las tres llaves del sistema legado. Por eso
/// los tres códigos llevan sufijo explícito, incluido <c>_comprador</c>, apartándose a propósito
/// de la convención de <see cref="IdentityCertificateAttachmentTipo"/> y de
/// <c>escritura_representante</c>, donde el código sin sufijo es el del comprador.
/// </para>
/// </summary>
public static class CamaraComercioAttachmentTipo
{
    public const string Prefijo = "camara_comercio";

    public const string Vendedor = "camara_comercio_vendedor";
    public const string Comprador = "camara_comercio_comprador";
    public const string Locatario = "camara_comercio_locatario";

    /// <summary>Los tres códigos, en el orden en que aparecen los actores en el asistente.</summary>
    public static readonly IReadOnlyList<string> Todos = [Vendedor, Comprador, Locatario];

    /// <summary>
    /// Tipo de adjunto del certificado para el actor en <paramref name="role"/>. Un rol desconocido
    /// devuelve <c>null</c> en vez de caer a un código por defecto: adivinar el rol equivaldría a
    /// dejar que el certificado de una parte satisficiera el requisito de otra, que es exactamente
    /// lo que estos códigos existen para impedir.
    /// </summary>
    public static string? For(string? role) => (role ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "vendedor" => Vendedor,
        "comprador" => Comprador,
        "locatario" => Locatario,
        _ => null,
    };

    /// <summary>¿<paramref name="tipo"/> es uno de los certificados de Cámara de Comercio por rol?</summary>
    public static bool Is(string? tipo) =>
        !string.IsNullOrWhiteSpace(tipo)
        && Todos.Contains(tipo.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Rol al que pertenece el <paramref name="tipo"/>, o <c>null</c> si no es uno de estos códigos.
    /// </summary>
    public static string? RoleOf(string? tipo) => (tipo ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        Vendedor => "vendedor",
        Comprador => "comprador",
        Locatario => "locatario",
        _ => null,
    };
}
