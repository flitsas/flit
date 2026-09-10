using Flit.Queries.Domain;

namespace Flit.Admin.Domain.OtQueries;

/// <summary>
/// Lo que es PROPIO del organismo en las consultas: sobre qué fechas se filtra, por qué columnas se
/// ordena y qué trae cada fila.
///
/// <para>El modelo de la consulta —condiciones, operadores, rangos con nombre, cobertura— vive en
/// <c>Flit.Queries.Domain</c> y lo comparte con las consultas de la empresa gestora. Aquí queda solo
/// lo que cambiaría de significado al mudarse de módulo.</para>
/// </summary>
public static class OtQueryDateField
{
    /// <summary>Primera vez que el trámite llegó al organismo. La lectura por defecto.</summary>
    public const string Radicacion = "radicacion";

    /// <summary>Cuándo se aprobó o rechazó. Deja fuera lo que sigue sin decidir.</summary>
    public const string Decision = "decision";

    /// <summary>
    /// Cuándo pasó a Aprobado, específicamente. Distinto de <see cref="Decision"/>: un trámite
    /// rechazado también decidió, pero nunca tiene esta fecha.
    /// </summary>
    public const string Aprobacion = "aprobacion";

    /// <summary>Último movimiento de cualquier tipo.</summary>
    public const string Actualizacion = "actualizacion";

    public static bool IsKnown(string? field) =>
        field is Radicacion or Decision or Aprobacion or Actualizacion;

    public static IReadOnlyList<QueryFieldOptionDto> Options { get; } =
    [
        new(Radicacion, "Fecha de radicación"),
        new(Decision, "Fecha de decisión"),
        new(Aprobacion, "Fecha de aprobación"),
        new(Actualizacion, "Última actualización"),
    ];
}

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
    // Nombre del tipo concreto. Se llamaba `Modalidad` y traía la familia: con dos tipos en el
    // catálogo eran lo mismo, con veintiuno la fila decía «Matrículas» tanto para una matrícula
    // inicial como para una de leasing. La familia sigue existiendo, pero solo como agrupación del
    // filtro — en pantalla el concepto es uno solo y se llama tipo de trámite.
    string TipoTramite,
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
    DateTimeOffset? AprobadoEn,
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

    public static IReadOnlyList<string> All { get; } =
        [Radicado, Creado, Decidido, Actualizado, Placa, Empresa, Referencia, Estado, Dias];

    public static bool IsKnown(string? sort) => sort is not null && All.Contains(sort, StringComparer.Ordinal);
}

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
    IReadOnlyList<QueryCoverageItemDto> Cobertura);
