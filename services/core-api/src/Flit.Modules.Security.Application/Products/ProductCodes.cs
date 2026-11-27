namespace Flit.Modules.Security.Application.Products;

/// <summary>
/// Códigos de producto de la FLIT Suite (contrato de plataforma v1, §1).
/// </summary>
/// <remarks>
/// HU #12898 (B-00) — el código es el mismo en la carpeta del servicio, el host, el schema, el
/// <c>aud</c> del token, los roles y la aplicación de Argo CD. Son <c>const</c> porque
/// <c>DomainContext</c> usa <see cref="Plataforma"/> como valor por defecto de un parámetro
/// (contrato §5), y ese valor tiene que ser constante. Resoluciones y Flotas quedan fuera de v1.
/// </remarks>
public static class ProductCodes
{
    /// <summary>Hub y login (<c>flitsas.online</c>).</summary>
    public const string Plataforma = "plataforma";

    /// <summary>Trámites (<c>tramites.flitsas.online</c>).</summary>
    public const string Tramites = "tramites";

    /// <summary>Gestión de comparendos (<c>comparendos.flitsas.online</c>).</summary>
    public const string Comparendos = "comparendos";

    /// <summary>Diagnóstico (<c>diagnostico.flitsas.online</c>).</summary>
    public const string Diagnostico = "diagnostico";

    /// <summary>
    /// Caja del organismo (Épica #14210, ADR-0082): portal de cobro del punto de atención. Vive en el portal de Persona
    /// Natural, no en core-api, así que su token solo llega a las rutas comunes de plataforma.
    /// </summary>
    public const string Caja = "caja";

    /// <summary>
    /// Impuestos (<c>impuestos.flitsas.online</c>, HU #14694, ADR-0093): liquidación del impuesto vehicular por placa y
    /// masiva. Su servicio es <c>core-impuestos</c> y es el único producto con aprovisionamiento en el proveedor.
    /// </summary>
    public const string Impuestos = "impuestos";

    /// <summary>Todos los códigos: los de v1 en el orden del contrato §1 y, al final, los agregados después.</summary>
    public static IReadOnlyList<string> All { get; } =
        [Plataforma, Tramites, Comparendos, Diagnostico, Caja, Impuestos];
}
