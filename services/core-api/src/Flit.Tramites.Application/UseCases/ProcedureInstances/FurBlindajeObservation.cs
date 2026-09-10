using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Bloque del párrafo 23 que declara QUÉ blindaje se solicita: el nivel instalado o su desmonte.
///
/// <para><b>Por qué el FUR tiene que decirlo.</b> El blindaje no tiene casilla propia en el numeral 3
/// —lo único que marca es «vehículo blindado SI/NO» en las características— así que la casilla sola
/// no distingue un nivel 1 de un nivel 3, y con el desmonte ni siquiera dice que hubo trámite: la
/// marca cae en <c>NO</c>, igual que en un vehículo que nunca estuvo blindado. Las observaciones son
/// el único sitio del formulario donde esa diferencia cabe.</para>
///
/// <para>Los literales son cortos a propósito: el recuadro tiene presupuesto contado
/// (<see cref="FurObservacionesComposer.PresupuestoCaracteres"/>) y lo automático entra íntegro
/// ANTES que el texto libre del gestor, así que cada carácter de más aquí se lo quita a él.</para>
///
/// <para>Fuente de los literales: <c>docs/ot/fur/REGLAS-NUMERAL-3-TRES-CAPAS.md</c> (tabla 1).</para>
/// </summary>
public static class FurBlindajeObservation
{
    /// <summary>
    /// Texto a anexar, o <c>null</c> si no hay nada que declarar.
    ///
    /// <para>Devuelve <c>null</c> con <see cref="BlindajeOpcion.Ninguna"/> —incluido un trámite de
    /// blindaje abierto antes de que la opción existiera—: la regla del artefacto es que si faltan
    /// datos se marca la casilla y NO se inventa el texto. Escribir «BLINDAJE NIVEL 1» por defecto
    /// declararía ante el organismo un nivel que nadie eligió.</para>
    /// </summary>
    public static string? Compose(BlindajeOpcion opcion) => opcion switch
    {
        BlindajeOpcion.Nivel1 => "BLINDAJE NIVEL 1.",
        BlindajeOpcion.Nivel2 => "BLINDAJE NIVEL 2.",
        BlindajeOpcion.Nivel3 => "BLINDAJE NIVEL 3.",
        BlindajeOpcion.Desmonte => "DESMONTE DE BLINDAJE.",
        _ => null,
    };

    /// <summary>Igual que <see cref="Compose(BlindajeOpcion)"/> leyendo el valor crudo de <c>field_values</c>.</summary>
    public static string? Compose(string? valorPersistido) =>
        Compose(BlindajeOpciones.Parse(valorPersistido));
}
