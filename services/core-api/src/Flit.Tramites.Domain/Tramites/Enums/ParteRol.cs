namespace Flit.Tramites.Domain.Tramites.Enums;

/// <summary>
/// Roles posibles de las partes intervinientes en un trámite (paridad <c>ParteRol</c> de Johan).
/// El modelo deja los 6 roles para futura extensión; el MVP solo configura
/// <see cref="Comprador"/> y <see cref="Vendedor"/>.
/// </summary>
public enum ParteRol
{
    Comprador,
    Vendedor,
    Heredero,
    Adjudicatario,
    RepresentanteLegal,
    Importador,
}
