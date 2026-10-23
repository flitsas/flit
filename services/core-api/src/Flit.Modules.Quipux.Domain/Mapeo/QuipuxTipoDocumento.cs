namespace Flit.Modules.Quipux.Domain.Mapeo;

/// <summary>
/// Traduce el tipo de documento de FLIT al código numérico que Quipux espera en
/// <c>tipoDocumentoPropietario</c> / <c>tipoDocumentoFuncionario</c>.
/// </summary>
/// <remarks>
/// <para>
/// Vocabulario de origen (FLIT 1.0 / actores 2.0):
/// letras cortas (<c>C</c>, <c>N</c>, …) y aliases canónicos del dominio
/// (<c>CC</c>, <c>NIT</c>, <c>CE</c>, …). Los códigos numéricos del catálogo Quipux
/// (CATALOGO_QX V3.2 hoja «Tipo documento») también se aceptan si ya vienen resueltos
/// (<c>"2"</c>, <c>"3"</c>).
/// </para>
/// <para>
/// CORRIGE UN BUG DE 1.0: allí <c>mapTypeDocument</c> devolvía el STRING
/// <c>"Se desconoce el tipo de documento"</c> cuando la letra no mapeaba, y ese texto viajaba tal
/// cual dentro de un campo numérico del payload. Aquí no hay valor centinela: o hay código, o no
/// se radica.
/// </para>
/// <para>
/// P0 #13912: sin aliases, un actor con <c>document_type = NIT</c> (vocabulario canónico de
/// <c>ActorPersonTypes</c>) fallaba con <c>tipo_documento_propietario_no_mapea</c> antes del POST.
/// </para>
/// </remarks>
public static class QuipuxTipoDocumento
{
    /// <summary>
    /// Códigos Quipux admitidos cuando el input ya es el número del catálogo (como string).
    /// </summary>
    private static readonly HashSet<int> CodigosCatalogo =
    [
        1,  // NN
        2,  // Cédula de ciudadanía
        3,  // NIT
        4,  // Cédula extranjería
        5,  // Tarjeta de identidad
        6,  // Pasaporte
        7,  // NUIP
        8,  // Carnet diplomático
        9,  // RUT
        20, // Sin documento
        21, // Registro civil
        22, // Cédula venezolana
        25, // Cédula ecuatoriana
    ];

    private static readonly Dictionary<string, int> Codigos = new(StringComparer.OrdinalIgnoreCase)
    {
        // Letras FLIT 1.0 (quipuxApiService.ts)
        ["C"] = 2,
        ["N"] = 3,
        ["X"] = 20,
        ["E"] = 4,
        ["P"] = 6,
        ["T"] = 5,
        ["U"] = 7,
        ["D"] = 8,

        // Aliases del dominio FLIT 2.0 / formularios / RUNT
        ["CC"] = 2,
        ["CEDULA"] = 2,
        ["NIT"] = 3,
        ["CE"] = 4,
        ["TI"] = 5,
        ["PA"] = 6,
        ["PAS"] = 6,
        ["PASAPORTE"] = 6,
        ["NUIP"] = 7,
        ["PEP"] = 7,
        ["CD"] = 8,
        ["RUT"] = 9,
        ["NN"] = 1,
        ["SD"] = 20,
        ["RC"] = 21,
        ["CV"] = 22,
        ["PPT"] = 7,
    };

    /// <summary>Claves (letras + aliases) que Quipux reconoce vía mapa. Útil para diagnóstico.</summary>
    public static IReadOnlyCollection<string> TiposSoportados => Codigos.Keys;

    /// <summary>
    /// Intenta traducir el tipo a código Quipux. Es la vía que debe usar el worker: un tipo
    /// desconocido no es una excepción, es una radicación que no arranca y que se reporta como
    /// error definitivo (reintentar no cambiaría el dato).
    /// </summary>
    /// <param name="tipo">Letra, alias o código numérico. Admite null, vacío, espacios y minúsculas.</param>
    /// <param name="codigo">Código Quipux; <c>0</c> cuando no mapea — valor que jamás debe enviarse.</param>
    /// <returns><c>true</c> solo si el tipo está en el mapa o es un código de catálogo válido.</returns>
    public static bool TryMap(string? tipo, out int codigo)
    {
        codigo = 0;

        if (string.IsNullOrWhiteSpace(tipo))
        {
            return false;
        }

        var clave = tipo.Trim();

        if (Codigos.TryGetValue(clave, out codigo))
        {
            return true;
        }

        // Ya resuelto como código Quipux (p. ej. settings del funcionario o dato migrado).
        if (int.TryParse(clave, out var numerico) && CodigosCatalogo.Contains(numerico))
        {
            codigo = numerico;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Traduce el tipo a código Quipux o falla. Para el punto donde el tipo YA fue validado y un
    /// desconocido sería un defecto de programación, no un dato malo del usuario.
    /// </summary>
    /// <exception cref="ArgumentException">El tipo no está en el mapa de Quipux.</exception>
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
