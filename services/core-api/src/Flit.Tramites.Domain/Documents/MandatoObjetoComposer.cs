namespace Flit.Tramites.Domain.Documents;

/// <summary>
/// HU #11206 — objeto del Contrato Privado de Mandato: el trámite y, si el vehículo se transforma
/// durante él, también las transformaciones.
///
/// <para><b>Por qué aquí y no en las plantillas:</b> ninguna de las diez plantillas del PO menciona
/// transformaciones; todas expresan el objeto con una sola variable (<c>{{tramite}}</c>). El requisito
/// no es tocar las plantillas, sino <b>componer esa variable</b> (D5). Por eso es una función pura del
/// dominio: la usan todas las familias de plantilla por igual, así que el objeto se redacta idéntico en
/// todas (AC4) sin que ninguna tenga que saber de esto.</para>
///
/// <para>Sin transformaciones el texto queda exactamente como hasta ahora (AC3).</para>
/// </summary>
public static class MandatoObjetoComposer
{
    /// <summary>Claves de <c>field_values</c> que marcan una transformación declarada en el trámite.</summary>
    public const string CambioColor = "cambio_color";
    public const string CambioCarroceria = "cambio_carroceria";
    public const string CambioCombustible = "cambio_combustible";

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
    /// Compone el objeto del contrato: <paramref name="nombreTramite"/> seguido de las transformaciones
    /// activas, separadas por comas y la última con «Y». Sin transformaciones devuelve el nombre del
    /// trámite tal cual.
    /// </summary>
    /// <param name="nombreTramite">Modalidad ya en mayúsculas (p. ej. «TRASPASO DE PROPIEDAD»).</param>
    /// <param name="transformaciones">Claves activas; se ignoran las desconocidas y las repetidas.</param>
    public static string Componer(string nombreTramite, IEnumerable<string>? transformaciones)
    {
        var nombre = nombreTramite?.Trim() ?? string.Empty;
        if (transformaciones is null)
        {
            return nombre;
        }

        var activas = new HashSet<string>(
            transformaciones.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var etiquetas = Etiquetas
            .Where(e => activas.Contains(e.Clave))
            .Select(e => e.Etiqueta)
            .ToList();

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
