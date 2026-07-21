namespace Flit.Modules.Quipux.Domain.Mapeo;

/// <summary>
/// Traduce el tipo de documento de FLIT (una letra: C, N, X, E, P, T, U, D) al código numérico
/// que Quipux espera en <c>tipoDocumentoPropietario</c>. El mapa es exactamente el de FLIT 1.0
/// (<c>quipuxApiService.ts</c>); las letras se conservan tal cual porque son el vocabulario del
/// dato de origen, no un catálogo propio.
/// </summary>
/// <remarks>
/// <para>
/// CORRIGE UN BUG DE 1.0: allí <c>mapTypeDocument</c> devolvía el STRING
/// <c>"Se desconoce el tipo de documento"</c> cuando la letra no mapeaba, y ese texto viajaba tal
/// cual dentro de un campo numérico del payload. El fallo no se veía en FLIT: se manifestaba como
/// un rechazo de Quipux sin causa evidente o, peor, como un trámite radicado con el propietario
/// mal identificado. Aquí no hay valor centinela posible: o hay código, o no se radica.
/// </para>
/// <para>
/// La tolerancia a mayúsculas y espacios no es cortesía: en 1.0 la entrada era
/// <c>vehicleOwnerDocumentType || ''</c>, o sea un valor de formulario sin normalizar.
/// </para>
/// </remarks>
public static class QuipuxTipoDocumento
{
    private static readonly Dictionary<string, int> Codigos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["C"] = 2,
        ["N"] = 3,
        ["X"] = 20,
        ["E"] = 4,
        ["P"] = 6,
        ["T"] = 5,
        ["U"] = 7,
        ["D"] = 8,
    };

    /// <summary>Letras que Quipux reconoce. Sirve para diagnóstico y para validar parametrización.</summary>
    public static IReadOnlyCollection<string> TiposSoportados => Codigos.Keys;

    /// <summary>
    /// Intenta traducir la letra a código Quipux. Es la vía que debe usar el worker: un tipo
    /// desconocido no es una excepción, es una radicación que no arranca y que se reporta como
    /// error definitivo (reintentar no cambiaría el dato).
    /// </summary>
    /// <param name="tipo">Letra del tipo de documento. Admite null, vacío, espacios y minúsculas.</param>
    /// <param name="codigo">Código Quipux; <c>0</c> cuando no mapea — valor que jamás debe enviarse.</param>
    /// <returns><c>true</c> solo si la letra está en el mapa.</returns>
    public static bool TryMap(string? tipo, out int codigo)
    {
        codigo = 0;

        if (string.IsNullOrWhiteSpace(tipo))
        {
            return false;
        }

        return Codigos.TryGetValue(tipo.Trim(), out codigo);
    }

    /// <summary>
    /// Traduce la letra a código Quipux o falla. Para el punto donde el tipo YA fue validado y un
    /// desconocido sería un defecto de programación, no un dato malo del usuario.
    /// </summary>
    /// <exception cref="ArgumentException">La letra no está en el mapa de Quipux.</exception>
    public static int Map(string tipo)
    {
        if (TryMap(tipo, out var codigo))
        {
            return codigo;
        }

        throw new ArgumentException(
            $"Tipo de documento '{tipo}' desconocido para Quipux. Soportados: {string.Join(", ", Codigos.Keys)}.",
            nameof(tipo));
    }
}
