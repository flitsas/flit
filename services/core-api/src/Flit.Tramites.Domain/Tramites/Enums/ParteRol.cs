namespace Flit.Tramites.Domain.Tramites.Enums;

/// <summary>
/// Roles posibles de las partes intervinientes en un trámite (paridad <c>ParteRol</c> de Johan).
/// El modelo deja roles extra para futura extensión; el MVP configura
/// <see cref="Comprador"/>/<see cref="Vendedor"/> (traspaso estándar) y
/// <see cref="Arrendadora"/>/<see cref="Locatario"/> (traspaso unilateral de leasing, HU #10590).
/// </summary>
public enum ParteRol
{
    Comprador,
    Vendedor,
    Heredero,
    Adjudicatario,
    RepresentanteLegal,
    Importador,

    /// <summary>
    /// Traspaso unilateral (HU #10590): compañía de leasing/arrendamiento que TRANSFIERE la propiedad
    /// amparada en el contrato de leasing. Es la parte que VALIDA IDENTIDAD (vía su representante legal).
    /// </summary>
    Arrendadora,

    /// <summary>
    /// Traspaso unilateral (HU #10590): parte que RECIBE el vehículo. Participa de forma DOCUMENTAL
    /// (paz y salvo + documento del locatario), sin validación biométrica.
    /// </summary>
    Locatario,
}
