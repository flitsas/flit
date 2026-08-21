using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Domain.Documents;

/// <summary>
/// HU #11206 — objeto del Contrato Privado de Mandato: el trámite y, si el vehículo se transforma
/// durante él, también las transformaciones. HU #11627 — si el trámite tiene prenda, también se nombra
/// (distinguiendo constitución de levantamiento), antes de las transformaciones.
///
/// <para><b>Por qué aquí y no en las plantillas:</b> ninguna de las diez plantillas del PO menciona
/// transformaciones ni prenda; todas expresan el objeto con una sola variable (<c>{{tramite}}</c>). El
/// requisito no es tocar las plantillas, sino <b>componer esa variable</b> (D5). Por eso es una función
/// pura del dominio: la usan todas las familias de plantilla por igual, así que el objeto se redacta
/// idéntico en todas (AC4) sin que ninguna tenga que saber de esto.</para>
///
/// <para>Sin transformaciones ni prenda el texto queda exactamente como hasta ahora (AC3/regresión
/// HU #11627).</para>
/// </summary>
public static class MandatoObjetoComposer
{
    /// <summary>Claves de <c>field_values</c> que marcan una transformación declarada en el trámite.</summary>
    public const string CambioColor = "cambio_color";
    public const string CambioCarroceria = "cambio_carroceria";
    public const string CambioCombustible = "cambio_combustible";

    /// <summary>
    /// HU #11627 — etiqueta de la prenda en el objeto del contrato cuando el FUR marca constitución
    /// (casilla 11). Literal pendiente de validación legal por el PO: se deja como constante para que
    /// cambiarla sea de una línea.
    /// </summary>
    public const string Prenda = "PRENDA";

    /// <summary>
    /// HU #11627 — etiqueta de la prenda en el objeto del contrato cuando el FUR marca levantamiento
    /// (casilla 12). Mismo motivo que <see cref="Prenda"/>: constante, sujeta a validación legal.
    /// </summary>
    public const string LevantamientoPrenda = "LEVANTAMIENTO DE PRENDA";

    /// <summary>
    /// Orden canónico de las transformaciones en el texto. Es fijo a propósito: el contrato de dos
    /// trámites con las mismas transformaciones debe leerse igual, sin depender de en qué orden las
    /// marcó el gestor.
    /// </summary>
    private static readonly (string Clave, string Etiqueta)[] Etiquetas =
    [
        (CambioColor, "CAMBIO DE COLOR"),
        (CambioCarroceria, "CAMBIO DE CARROCERÍA"),
        (CambioCombustible, "CAMBIO DE COMBUSTIBLE"),
    ];

    /// <summary>
    /// Compone el objeto del contrato: <paramref name="nombreTramite"/> seguido de la prenda (si aplica)
    /// y las transformaciones activas, separadas por comas y la última con «Y». Sin prenda ni
    /// transformaciones devuelve el nombre del trámite tal cual.
    /// </summary>
    /// <param name="nombreTramite">Modalidad ya en mayúsculas (p. ej. «TRASPASO DE PROPIEDAD»).</param>
    /// <param name="transformaciones">Claves activas; se ignoran las desconocidas y las repetidas.</param>
    /// <param name="prendaMarking">
    /// HU #11627 — marcación de prenda del trámite (agregado <c>ProcedureInstancePrenda</c>, fuera de
    /// <c>field_values</c> por diseño del Feature #10585, ya resuelta a <see cref="FurPrendaMarking"/>).
    /// <see cref="FurPrendaMarking.Constitucion"/> nombra <see cref="Prenda"/>;
    /// <see cref="FurPrendaMarking.Levantamiento"/> nombra <see cref="LevantamientoPrenda"/>;
    /// <see cref="FurPrendaMarking.Ninguna"/> no agrega nada. Se nombra primero entre los elementos que
    /// siguen al nombre del trámite, antes de las transformaciones.
    /// </param>
    public static string Componer(
        string nombreTramite,
        IEnumerable<string>? transformaciones,
        FurPrendaMarking prendaMarking = FurPrendaMarking.Ninguna)
    {
        var nombre = nombreTramite?.Trim() ?? string.Empty;

        var etiquetas = new List<string>();
        switch (prendaMarking)
        {
            case FurPrendaMarking.Constitucion:
                etiquetas.Add(Prenda);
                break;
            case FurPrendaMarking.Levantamiento:
                etiquetas.Add(LevantamientoPrenda);
                break;
            case FurPrendaMarking.Ambos:
                etiquetas.Add(LevantamientoPrenda);
                etiquetas.Add(Prenda);
                break;
        }

        if (transformaciones is not null)
        {
            var activas = new HashSet<string>(
                transformaciones.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()),
                StringComparer.OrdinalIgnoreCase);

            etiquetas.AddRange(
                Etiquetas.Where(e => activas.Contains(e.Clave)).Select(e => e.Etiqueta));
        }

        if (etiquetas.Count == 0)
        {
            return nombre;
        }

        // El nombre del trámite es el primer elemento de la enumeración: «A, B Y C».
        var todos = new List<string>(etiquetas.Count + 1) { nombre };
        todos.AddRange(etiquetas);

        var ultima = todos[^1];
        var previas = string.Join(", ", todos.Take(todos.Count - 1));
        return $"{previas} Y {ultima}";
    }
}
