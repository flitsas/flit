namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Nombre del conductor ya separado en sus cuatro componentes. Los cuatro campos son
/// no-nulos (cadena vacía cuando el RUNT no tiene ese componente, p. ej. una persona con un
/// solo nombre o un solo apellido).
/// </summary>
public sealed record DriverNames(string FirstName, string SecondName, string FirstLastName, string SecondLastName)
{
    public static readonly DriverNames Empty = new(string.Empty, string.Empty, string.Empty, string.Empty);

    /// <summary>Se resolvió algo utilizable. Un apellido sin nombre (o al revés) sigue sirviendo.</summary>
    public bool HasAny => FirstName.Length > 0 || FirstLastName.Length > 0;

    /// <summary>Nombres de pila juntos ("JOSE GABRIEL JAIME").</summary>
    public string GivenNames => DriverNameResolver.JoinWords(FirstName, SecondName);

    /// <summary>Apellidos juntos ("CARDENAS GUTIERREZ") — el <c>person_last_name</c> histórico.</summary>
    public string Surnames => DriverNameResolver.JoinWords(FirstLastName, SecondLastName);

    public string FullName => DriverNameResolver.JoinWords(FirstName, SecondName, FirstLastName, SecondLastName);
}

/// <summary>
/// Resuelve el nombre del conductor a partir de lo que entrega cada proveedor de RUNT, para que
/// <c>kyverum_runt_conductor</c> y <c>verifik_conductor</c> hidraten exactamente los mismos campos.
/// </summary>
/// <remarks>
/// <para><b>Por qué existe:</b> el RUNT empezó a enmascarar los campos de nombre "de display"
/// (Kyverum los devuelve como <c>"S****L C****S G****Z"</c> en <c>persona.nombres</c>/<c>apellidos</c>)
/// y a publicar los reales en campos desglosados (<c>identidad</c>, <c>persona.primerNombre</c>, …).
/// Leer los enmascarados dejaba el nombre del actor en basura: el front les quita los asteriscos al
/// sanear el campo y terminaba guardando "SL CS GZ". Por eso <see cref="Clean"/> descarta cualquier
/// valor con <c>*</c> en vez de intentar limpiarlo — un nombre enmascarado no es un nombre.</para>
/// <para>El orden de preferencia es siempre el mismo: campos desglosados del proveedor → campos
/// combinados → separación heurística del nombre completo (último recurso). Portado del arreglo
/// equivalente de la v1 (<c>driverNameResolver.ts</c>) para que ambas versiones partan igual.</para>
/// </remarks>
public static class DriverNameResolver
{
    /// <summary>
    /// Partículas que no forman un apellido/nombre por sí solas: se pegan a la palabra siguiente
    /// ("DE JESUS", "DE LA CRUZ"). Sin esto, "HECTOR DE JESUS CARDENAS LARREA" se parte mal.
    /// </summary>
    private static readonly string[] Particles = ["DE", "DEL", "LA", "LAS", "LOS"];

    /// <summary>
    /// Normaliza un valor del proveedor: mayúsculas, sin espacios redundantes. Devuelve vacío si
    /// el valor viene enmascarado (contiene <c>*</c>) — es inservible para formularios y documentos,
    /// y propagarlo es peor que no tener el dato.
    /// </summary>
    public static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = string.Join(' ', value.Trim().ToUpperInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return normalized.Contains('*', StringComparison.Ordinal) ? string.Empty : normalized;
    }

    /// <summary>
    /// Resuelve desde los cuatro campos desglosados del proveedor (Kyverum: <c>identidad</c> o
    /// <c>persona</c>). Es la ruta preferida: no adivina nada.
    /// </summary>
    public static DriverNames FromParts(
        string? firstName, string? secondName, string? firstLastName, string? secondLastName)
    {
        var names = new DriverNames(
            Clean(firstName), Clean(secondName), Clean(firstLastName), Clean(secondLastName));

        return names.HasAny ? names : DriverNames.Empty;
    }

    /// <summary>
    /// Resuelve desde los campos combinados de Verifik: <c>firstName</c> trae TODOS los nombres de
    /// pila ("JOSE GABRIEL JAIME") y <c>lastName</c> todos los apellidos. <c>primerApellido</c> y
    /// <c>segundoApellido</c> solo llegan en algunas respuestas; cuando están, mandan ellos, y si no
    /// se agrupan los apellidos respetando partículas.
    /// </summary>
    public static DriverNames FromCombined(
        string? givenNames, string? surnames, string? firstLastName = null, string? secondLastName = null)
    {
        var given = GroupWords(Clean(givenNames));

        var explicitFirstLast = Clean(firstLastName);
        var (first, second) = explicitFirstLast.Length > 0
            ? (explicitFirstLast, Clean(secondLastName))
            : SplitSurnames(GroupWords(Clean(surnames)));

        var names = new DriverNames(
            given.Count > 0 ? given[0] : string.Empty,
            given.Count > 1 ? string.Join(' ', given.Skip(1)) : string.Empty,
            first,
            second);

        return names.HasAny ? names : DriverNames.Empty;
    }

    /// <summary>
    /// Último recurso: separa un nombre completo sin ninguna pista del proveedor. Se agrupa por
    /// partículas y luego se reparte con la convención colombiana — con tres grupos se asume un
    /// nombre y dos apellidos ("SAMUEL CARDENAS GUTIERREZ"), y de cuatro en adelante los dos últimos
    /// grupos son los apellidos y todo lo anterior son nombres.
    /// </summary>
    public static DriverNames FromFullName(string? fullName)
    {
        var groups = GroupWords(Clean(fullName));

        return groups.Count switch
        {
            0 => DriverNames.Empty,
            1 => new DriverNames(groups[0], string.Empty, string.Empty, string.Empty),
            2 => new DriverNames(groups[0], string.Empty, groups[1], string.Empty),
            3 => new DriverNames(groups[0], string.Empty, groups[1], groups[2]),
            _ => new DriverNames(
                groups[0],
                string.Join(' ', groups.Skip(1).Take(groups.Count - 3)),
                groups[^2],
                groups[^1]),
        };
    }

    /// <summary>
    /// Nombre completo a publicar: el del proveedor si es utilizable, y si viene vacío o enmascarado
    /// se recompone desde los componentes ya resueltos. Evita propagar un <c>fullName</c> inservible
    /// cuando los desglosados sí llegaron bien.
    /// </summary>
    public static string ResolveFullName(string? providerFullName, DriverNames names)
    {
        var cleaned = Clean(providerFullName);
        return cleaned.Length > 0 ? cleaned : names.FullName;
    }

    /// <summary>
    /// Hidrata los campos de nombre, idénticos para los dos proveedores. <c>person_first_name</c> es
    /// el PRIMER nombre (no todos los de pila) y <c>person_last_name</c> se conserva con los dos
    /// apellidos juntos, que es lo que consume la ficha del actor. Los componentes vacíos no se
    /// hidratan: un campo ausente y uno en blanco significan lo mismo aguas abajo.
    /// </summary>
    public static void AddHydratedNames(List<HydratedField> fields, DriverNames names, string fullName)
    {
        Add(fields, "person_full_name", fullName);
        Add(fields, "person_first_name", names.FirstName);
        Add(fields, "person_second_name", names.SecondName);
        Add(fields, "person_first_last_name", names.FirstLastName);
        Add(fields, "person_second_last_name", names.SecondLastName);
        Add(fields, "person_last_name", names.Surnames);
    }

    private static void Add(List<HydratedField> fields, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            fields.Add(new HydratedField(key, value, null));
    }

    internal static string JoinWords(params string[] parts) =>
        string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    /// <summary>
    /// Agrupa palabras pegando las partículas a la siguiente: "DE JESUS CARDENAS" → ["DE JESUS", "CARDENAS"].
    /// Una partícula final sin palabra que la siga se queda como grupo propio (dato malformado, no se pierde).
    /// </summary>
    private static List<string> GroupWords(string value)
    {
        var groups = new List<string>();
        if (value.Length == 0)
            return groups;

        var current = new List<string>();
        foreach (var word in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            current.Add(word);
            if (!Particles.Contains(word, StringComparer.Ordinal))
            {
                groups.Add(string.Join(' ', current));
                current.Clear();
            }
        }

        if (current.Count > 0)
            groups.Add(string.Join(' ', current));

        return groups;
    }

    private static (string First, string Second) SplitSurnames(List<string> groups) => groups.Count switch
    {
        0 => (string.Empty, string.Empty),
        1 => (groups[0], string.Empty),
        _ => (groups[0], string.Join(' ', groups.Skip(1))),
    };
}
