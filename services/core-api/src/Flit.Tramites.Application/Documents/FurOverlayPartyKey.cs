namespace Flit.Tramites.Application.Documents;

/// <summary>
/// Llave de firma/sello en el overlay FUR cuando un lado tiene varios actores (ADR-0053).
/// El ordinal 1 conserva la llave histórica del rol (<c>comprador</c>/<c>vendedor</c>) para no
/// romper mandato, compraventa ni sellos ya indexados por rol.
/// </summary>
public static class FurOverlayPartyKey
{
    public static string For(string rol, int ordinal) =>
        ordinal <= 1 ? rol : $"{rol}:{ordinal}";
}
