namespace Flit.Admin.Domain.OtQueries;

/// <summary>
/// Consultas del organismo: el usuario arma su propia búsqueda sobre los trámites, la guarda y la
/// exporta.
///
/// <para><b>Por qué un catálogo de campos y no una consulta libre.</b> La tentación es aceptar algo
/// parecido a SQL y traducirlo. No se hace: el cliente manda IDENTIFICADORES de una lista cerrada
/// (<see cref="OtQueryFieldCatalog"/>) y el servidor decide cómo se resuelve cada uno. Así ningún
/// texto del cliente llega nunca a la consulta, agregar un campo es tocar un solo archivo, y el
/// constructor de la UI se pinta a partir del catálogo en vez de repetirlo.</para>
/// </summary>
public static class OtQueryFieldKind
{
    /// <summary>Texto libre. Admite una lista de valores exactos — el caso de pegar placas desde Excel.</summary>
    public const string Texto = "texto";

    /// <summary>Lista cerrada de opciones (estados, tipos, empresas).</summary>
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
public static class OtQueryOperator
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

/// <summary>Una opción de un campo de lista cerrada.</summary>
public sealed record OtQueryFieldOptionDto(string Value, string Label);

/// <summary>
/// Un campo consultable, tal y como lo ve la UI.
///
/// <para><see cref="Options"/> viene vacío en los campos cuyo catálogo depende del organismo
/// (empresas): ésos los rellena el endpoint con los datos del tenant. <see cref="Hint"/> es la letra
/// pequeña que evita la pregunta de soporte — por ejemplo, que «comprador» busca por nombre y
/// también por documento.</para>
/// </summary>
public sealed record OtQueryFieldDto(
    string Id,
    string Label,
    string Kind,
    string Group,
    IReadOnlyList<string> Operators,
    IReadOnlyList<OtQueryFieldOptionDto> Options,
    string? Hint,
    bool AdmiteLista);

/// <summary>Una condición de la consulta. <see cref="Values"/> vacío solo es válido con operadores unarios.</summary>
public sealed record OtQueryCondition(
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
/// Bogotá en cada ejecución. Solo <see cref="OtQueryRangePreset.Personalizado"/> guarda extremos
/// fijos, y es la excepción que el usuario pide a sabiendas.</para>
/// </summary>
public sealed record OtQueryDateFilter(
    string Campo,
    string Preset,
    DateOnly? From = null,
    DateOnly? To = null);

/// <summary>Sobre qué fecha del trámite se aplica el rango.</summary>
public static class OtQueryDateField
{
    /// <summary>Primera vez que el trámite llegó al organismo. La lectura por defecto.</summary>
    public const string Radicacion = "radicacion";

    /// <summary>Cuándo se aprobó o rechazó. Deja fuera lo que sigue sin decidir.</summary>
    public const string Decision = "decision";

    /// <summary>Último movimiento de cualquier tipo.</summary>
    public const string Actualizacion = "actualizacion";

    public static bool IsKnown(string? field) => field is Radicacion or Decision or Actualizacion;

    public static IReadOnlyList<OtQueryFieldOptionDto> Options { get; } =
    [
        new(Radicacion, "Fecha de radicación"),
        new(Decision, "Fecha de decisión"),
        new(Actualizacion, "Última actualización"),
    ];
}

/// <summary>
/// Rangos con nombre. Se guardan en relativo para que la consulta siga viva; ver
/// <see cref="OtQueryDateFilter"/>.
/// </summary>
public static class OtQueryRangePreset
{
    public const string Hoy = "hoy";
    public const string Ultimos7 = "ultimos_7";
    public const string Ultimos30 = "ultimos_30";
    public const string Ultimos90 = "ultimos_90";
    public const string MesActual = "mes_actual";
    public const string MesAnterior = "mes_anterior";
    public const string AnioActual = "anio_actual";
    public const string Personalizado = "personalizado";

    public static bool IsKnown(string? preset) => preset is
        Hoy or Ultimos7 or Ultimos30 or Ultimos90
        or MesActual or MesAnterior or AnioActual or Personalizado;

    public static IReadOnlyList<OtQueryFieldOptionDto> Options { get; } =
    [
        new(Hoy, "Hoy"),
        new(Ultimos7, "Últimos 7 días"),
        new(Ultimos30, "Últimos 30 días"),
        new(Ultimos90, "Últimos 90 días"),
        new(MesActual, "Mes actual"),
        new(MesAnterior, "Mes anterior"),
        new(AnioActual, "Año actual"),
        new(Personalizado, "Rango propio"),
    ];

    /// <summary>
    /// Resuelve el rango contra el día indicado (el de Bogotá, que lo pone el repositorio).
    /// Un preset desconocido cae a los últimos 30 días en vez de reventar: una consulta guardada
    /// con un preset que dejó de existir debe seguir abriendo.
    /// </summary>
    public static (DateOnly From, DateOnly To) Resolve(OtQueryDateFilter filter, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(filter);

        switch (filter.Preset)
        {
            case Hoy:
                return (today, today);
            case Ultimos7:
                return (today.AddDays(-6), today);
            case Ultimos90:
                return (today.AddDays(-89), today);
            case MesActual:
                return (new DateOnly(today.Year, today.Month, 1), today);
            case MesAnterior:
                var primeroDeEste = new DateOnly(today.Year, today.Month, 1);
                var primeroDelAnterior = primeroDeEste.AddMonths(-1);
                return (primeroDelAnterior, primeroDeEste.AddDays(-1));
            case AnioActual:
                return (new DateOnly(today.Year, 1, 1), today);
            case Personalizado:
                // Sin extremos guardados no hay nada que respetar: se cae al defecto en vez de
                // devolver un rango vacío que se leería como «no hay trámites».
                var from = filter.From ?? today.AddDays(-29);
                var to = filter.To ?? today;
                return from > to ? (to, from) : (from, to);
            default:
                return (today.AddDays(-29), today);
        }
    }
}

/// <summary>
/// Una consulta completa: qué fechas, qué condiciones, qué columnas y en qué orden. Es lo que se
/// persiste al guardar y lo que viaja en el enlace compartible.
/// </summary>
public sealed record OtQueryDefinition(
    OtQueryDateFilter Fechas,
    IReadOnlyList<OtQueryCondition> Condiciones,
    IReadOnlyList<string> Columnas,
    string? SortBy = null,
    bool Descending = true);

/// <summary>Topes de la consulta.</summary>
public static class OtQueryLimits
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
}

/// <summary>Qué pasó con un valor que el usuario pidió explícitamente.</summary>
public static class OtQueryCoverageResult
{
    public const string Encontrado = "encontrado";

    /// <summary>Existe en el organismo, pero otra condición lo dejó fuera. Se dice cuál.</summary>
    public const string Excluido = "excluido";

    /// <summary>No hay ningún trámite con ese valor en este organismo.</summary>
    public const string NoExiste = "no_existe";
}

/// <summary>
/// Una línea del informe de cobertura: qué pasó con cada placa o VIN que el usuario pidió por
/// nombre.
///
/// <para>Es la diferencia entre un reporte en el que se confía y uno en el que no. Si alguien pega
/// dos placas, marca «tiene LT cargada» y le sale una sola fila, sin esto la lectura natural es «se
/// perdió un dato». Con esto la respuesta está en pantalla: la otra existe y la dejó fuera ese
/// filtro, o sencillamente no está en este organismo.</para>
/// </summary>
public sealed record OtQueryCoverageItemDto(
    string Campo,
    string Valor,
    string Resultado,
    string? MotivoCampo,
    string? Motivo);

/// <summary>
/// Una fila del resultado: un trámite.
///
/// <para><b>Una fila = un trámite, siempre.</b> Filtrar por comprador o por prenda toca tablas
/// hijas, y un join directo multiplicaría la fila del padre por cada hija que coincida: un trámite
/// con dos actores saldría dos veces y todos los totales quedarían inflados. Por eso las
/// condiciones sobre hijas se resuelven como «existe», nunca como cruce.</para>
/// </summary>
public sealed record OtQueryRowDto(
    Guid ProcedureInstanceId,
    string ReferenceNumber,
    string? Placa,
    string? Vin,
    Guid ClientTenantId,
    string ClientTenantName,
    string Modalidad,
    string Status,
    string EstadoOt,
    bool Prioritario,
    bool SubsanacionActiva,
    string? Comprador,
    string? Vendedor,
    bool TienePrenda,
    string? AcreedorPrenda,
    bool TieneLicenciaTransito,
    IReadOnlyList<string> Transformaciones,
    DateTimeOffset CreadoEn,
    DateTimeOffset? RadicadoEn,
    DateTimeOffset? UltimaRadicacionEn,
    DateTimeOffset? DecididoEn,
    DateTimeOffset? ActualizadoEn,
    string? DecididoPor,
    double? HorasHastaDecision,
    double? DiasEnOrganismo,
    int Devoluciones,
    IReadOnlyList<string> CausalesUltimoRechazo);

/// <summary>Campos por los que se puede ordenar. Lista cerrada: un campo libre sería inyección de orden.</summary>
public static class OtQuerySort
{
    public const string Radicado = "radicado";
    public const string Creado = "creado";
    public const string Decidido = "decidido";
    public const string Actualizado = "actualizado";
    public const string Placa = "placa";
    public const string Empresa = "empresa";
    public const string Referencia = "referencia";
    public const string Estado = "estado";
    public const string Dias = "dias";

    public static bool IsKnown(string? sort) => sort is
        Radicado or Creado or Decidido or Actualizado
        or Placa or Empresa or Referencia or Estado or Dias;
}

/// <summary>Parámetros de ejecución: la definición más la página que se quiere ver.</summary>
public sealed record OtQueryRequest(
    OtQueryDefinition Definition,
    int Page,
    int PageSize);

/// <summary>
/// El resultado de una consulta.
///
/// <para><see cref="Total"/> es el universo completo, no las filas devueltas: el contador de la
/// cabecera nunca describe solo la página visible, y el export recorre todas las páginas.</para>
/// </summary>
public sealed record OtQueryResultDto(
    int Total,
    int Page,
    int PageSize,
    DateOnly Desde,
    DateOnly Hasta,
    int TotalPeriodoAnterior,
    IReadOnlyList<OtQueryRowDto> Filas,
    IReadOnlyList<OtQueryCoverageItemDto> Cobertura);

/// <summary>Una consulta guardada del usuario.</summary>
public sealed record OtSavedQueryDto(
    Guid Id,
    string Nombre,
    string? Descripcion,
    bool DeFabrica,
    OtQueryDefinition Definition,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>Alta o edición de una consulta guardada.</summary>
public sealed record OtSavedQueryInput(
    string Nombre,
    string? Descripcion,
    OtQueryDefinition Definition);
