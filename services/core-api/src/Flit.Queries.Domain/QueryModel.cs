namespace Flit.Queries.Domain;

/// <summary>
/// El modelo de una consulta armada por el usuario, sin dominio propio.
///
/// <para><b>Por qué un catálogo de campos y no una consulta libre.</b> La tentación es aceptar algo
/// parecido a SQL y traducirlo. No se hace: el cliente manda IDENTIFICADORES de una lista cerrada
/// (<see cref="IQueryFieldCatalog"/>) y el servidor decide cómo se resuelve cada uno. Así ningún
/// texto del cliente llega nunca a la consulta, agregar un campo es tocar un solo archivo, y el
/// constructor de la UI se pinta a partir del catálogo en vez de repetirlo.</para>
///
/// <para><b>Por qué esto vive en un proyecto aparte.</b> Lo usan dos módulos con dominios distintos
/// —el organismo de tránsito consulta lo que le radican, la empresa gestora consulta lo que
/// tramita— y hay piezas que NO pueden divergir entre ambos: qué significa «últimos 30 días», cómo
/// se compara una placa pegada desde Excel, y qué se le responde al usuario cuando un valor que
/// pidió no sale. Dos copias de esas reglas no fallan con un error: hacen que dos informes del
/// mismo producto se contradigan sobre el mismo trámite.</para>
/// </summary>
public static class QueryFieldKind
{
    /// <summary>Texto libre. Admite una lista de valores exactos — el caso de pegar placas desde Excel.</summary>
    public const string Texto = "texto";

    /// <summary>Lista cerrada de opciones (estados, tipos, organismos).</summary>
    public const string Opcion = "opcion";

    /// <summary>Sí / no.</summary>
    public const string Booleano = "booleano";
}

/// <summary>
/// Operadores admitidos. Lista deliberadamente corta.
///
/// <para>No hay grupos anidados de Y/O. El multivalor de un mismo campo YA es un «o» (placa en A, B,
/// C), que es donde la gente de verdad lo necesita; el árbol de condiciones es justo la parte de
/// estos constructores donde el usuario se pierde. Si algún día hace falta, se agrega — pero
/// empezar por ahí es garantizar que nadie lo use.</para>
/// </summary>
public static class QueryOperator
{
    /// <summary>Coincide con cualquiera de los valores (exacto, normalizado).</summary>
    public const string EsAlguno = "es_alguno";

    /// <summary>No coincide con ninguno de los valores.</summary>
    public const string NoEsNinguno = "no_es_ninguno";

    /// <summary>Contiene el texto. Un solo valor.</summary>
    public const string Contiene = "contiene";

    public const string EstaVacio = "esta_vacio";

    public const string NoEstaVacio = "no_esta_vacio";

    public static bool IsKnown(string? op) => op is
        EsAlguno or NoEsNinguno or Contiene or EstaVacio or NoEstaVacio;

    /// <summary>Operadores que no llevan valores: mandarlos con valores es un error del cliente, no un filtro.</summary>
    public static bool IsUnary(string op) => op is EstaVacio or NoEstaVacio;
}

/// <summary>
/// Una opción de un campo de lista cerrada.
///
/// <para><see cref="Group"/> es el encabezado bajo el que la UI agrupa la opción, y es opcional: los
/// campos cuyo catálogo es una lista corta y plana (estados, sí/no) no lo llevan. Existe porque el
/// tipo de trámite dejó de ser una lista de dos: con veintiún tipos repartidos en tres familias, una
/// lista plana obliga a quien consulta a saberse de memoria qué tipo pertenece a qué familia.</para>
/// </summary>
public sealed record QueryFieldOptionDto(string Value, string Label, string? Group = null);

/// <summary>
/// Un campo consultable, tal y como lo ve la UI.
///
/// <para><see cref="Options"/> viene vacío en los campos cuyo catálogo depende de los datos del
/// tenant (empresas, revisores, organismos): ésos los rellena el repositorio. <see cref="Hint"/> es
/// la letra pequeña que evita la pregunta de soporte — por ejemplo, que «comprador» busca por
/// nombre y también por documento.</para>
/// </summary>
public sealed record QueryFieldDto(
    string Id,
    string Label,
    string Kind,
    string Group,
    IReadOnlyList<string> Operators,
    IReadOnlyList<QueryFieldOptionDto> Options,
    string? Hint,
    bool AdmiteLista);

/// <summary>Una condición de la consulta. <see cref="Values"/> vacío solo es válido con operadores unarios.</summary>
public sealed record QueryCondition(
    string FieldId,
    string Operator,
    IReadOnlyList<string> Values);

/// <summary>
/// El rango de la consulta y sobre qué fecha aplica.
///
/// <para><b>Qué fecha</b> es una pregunta real, no un detalle: «los traspasos de julio» significa
/// cosas distintas según se mire la radicación o la decisión, y casi todas las herramientas de
/// reportes se queman por asumir una sola. Aquí se elige explícitamente.</para>
///
/// <para><b>El preset se guarda, no las fechas.</b> Una consulta guardada con «1 al 31 de agosto»
/// miente en septiembre. Se persiste «últimos 30 días» y el servidor lo resuelve contra el día de
/// Bogotá en cada ejecución. Solo <see cref="QueryRangePreset.Personalizado"/> guarda extremos
/// fijos, y es la excepción que el usuario pide a sabiendas.</para>
/// </summary>
public sealed record QueryDateFilter(
    string Campo,
    string Preset,
    DateOnly? From = null,
    DateOnly? To = null);

/// <summary>
/// Una consulta completa: qué fechas, qué condiciones, qué columnas y en qué orden. Es lo que se
/// persiste al guardar y lo que viaja en el enlace compartible.
/// </summary>
public sealed record QueryDefinition(
    QueryDateFilter Fechas,
    IReadOnlyList<QueryCondition> Condiciones,
    IReadOnlyList<string> Columnas,
    string? SortBy = null,
    bool Descending = true);

/// <summary>Topes de la consulta.</summary>
public static class QueryLimits
{
    public const int MaxPageSize = 200;
    public const int DefaultPageSize = 50;

    /// <summary>
    /// Valores por condición. Pegar una columna de Excel es el caso de uso, pero una lista sin
    /// tope convierte cualquier consulta en un escaneo y el aviso de cobertura en una pared de texto.
    /// </summary>
    public const int MaxValoresPorCondicion = 500;

    public const int MaxCondiciones = 20;

    /// <summary>Consultas guardadas por usuario. Pasado esto la lista deja de ser navegable.</summary>
    public const int MaxConsultasGuardadas = 50;

    /// <summary>Trámites que se traen cuando la consulta no acota por identificador. Tope de cordura.</summary>
    public const int MaxUniverso = 20_000;
}

/// <summary>Parámetros de ejecución: la definición más la página que se quiere ver.</summary>
public sealed record QueryRequest(
    QueryDefinition Definition,
    int Page,
    int PageSize);
