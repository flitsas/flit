namespace Flit.Modules.Platform.Application.Hosts;

/// <summary>
/// Hosts de los productos (contrato de plataforma v1, §1): la raíz o <c>&lt;ambiente&gt;.&lt;raíz&gt;</c>
/// es la plataforma y <c>&lt;ambiente&gt;.&lt;producto&gt;.&lt;raíz&gt;</c> cada producto. Lo implementa la API
/// con la configuración <c>Suite:Hosts</c>. Los dominios de red por producto llegan con B-08.
/// </summary>
public interface IProductHosts
{
    /// <summary>URL absoluta del producto en este ambiente.</summary>
    string UrlFor(string productCode);

    /// <summary>Producto que sirve <paramref name="host"/>, o <c>null</c> si no es un host FLIT conocido.</summary>
    string? ProductForHost(string? host);
}
