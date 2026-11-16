namespace Flit.Tramites.Application.Identity;

/// <summary>
/// Arma la URL completa del enlace de captura manual a partir del token en claro (HU #14305). Es la ÚNICA fuente de esa URL: la usan
/// el correo al titular y la copia del enlace para el Super Admin, así ambos apuntan al mismo lugar.
/// </summary>
public interface IManualCaptureLinkUrlBuilder
{
    string Build(string token);
}
