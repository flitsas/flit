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

    /// <summary>Producto de prueba de la plantilla (solo DEV).</summary>
    public const string Demo = "demo";

    /// <summary>Todos los códigos de v1, en el orden del contrato §1.</summary>
    public static IReadOnlyList<string> All { get; } =
        [Plataforma, Tramites, Comparendos, Diagnostico, Demo];
}
