using Flit.Tramites.Domain.Tramites.Enums;

namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>Parte interviniente requerida en un journey (paridad <c>ParteRequerida</c> de Johan).</summary>
public sealed record ParteRequerida(
    ParteRol Rol,
    string Label,
    bool Obligatorio,
    string? Ayuda = null);

/// <summary>
/// Configuración del adquirente: la parte primaria que captura el paso de "comprador"
/// del wizard (paridad <c>AdquirenteConfig</c> de Johan).
/// </summary>
public sealed record AdquirenteConfig(
    ParteRol Rol,
    string Label,
    string? Ayuda = null);
