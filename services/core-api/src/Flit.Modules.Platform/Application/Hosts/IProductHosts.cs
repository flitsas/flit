namespace Flit.Modules.Platform.Application.Hosts;

/// <summary>
/// Hosts de los productos (contrato de plataforma v1, §1): la raíz o <c>&lt;ambiente&gt;.&lt;raíz&gt;</c>
/// es la plataforma y <c>&lt;ambiente&gt;.&lt;producto&gt;.&lt;raíz&gt;</c> cada producto. Lo implementa la API
/// con la configuración <c>Suite:Hosts</c>. Los dominios de red por producto llegan con B-08.
/// </summary>
public interface IProductHosts
{
    /// <summary>URL absoluta de la app del producto en este ambiente (su origen: retornos del login, emisor).</summary>
    string UrlFor(string productCode);

    /// <summary>
    /// Adónde lleva el menú de productos: la app, o su pantalla «Próximamente» del hub si todavía no está desplegada en
    /// este ambiente.
    /// </summary>
    string LinkFor(string productCode);

    /// <summary><c>true</c> si el producto todavía no está desplegado en este ambiente (su enlace es «Próximamente»).</summary>
    bool IsComingSoon(string productCode);

    /// <summary>Producto que sirve <paramref name="host"/>, o <c>null</c> si no es un host FLIT conocido.</summary>
    string? ProductForHost(string? host);
}
