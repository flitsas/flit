using Flit.Queries.Domain;

namespace Flit.Analytics.Application.CompanyQueries;

/// <summary>
/// Sobre qué fecha del trámite se aplica el rango, visto desde la empresa gestora.
///
/// <para>Son otras que las del organismo y por una razón concreta: la gestora vive el trámite desde
/// antes —lo arma en borrador— y el organismo solo lo conoce desde que se lo radican. «Los traspasos
/// de julio» es una pregunta distinta según se cuente por cuándo se empezó a preparar o por cuándo
/// se entregó.</para>
/// </summary>
public static class CompanyQueryDateField
{
    /// <summary>Cuándo se creó el trámite en FLIT. La lectura por defecto de quien lo gestiona.</summary>
    public const string Creacion = "creacion";

    /// <summary>Cuándo se entregó al organismo. Deja fuera lo que sigue en borrador.</summary>
    public const string Envio = "envio";

    /// <summary>Cuándo quedó cerrado. Deja fuera lo que sigue abierto.</summary>
    public const string Cierre = "cierre";

    /// <summary>Último movimiento de cualquier tipo.</summary>
    public const string Actualizacion = "actualizacion";

    public static IReadOnlyList<QueryFieldOptionDto> Options { get; } =
    [
        new(Creacion, "Fecha de creación"),
        new(Envio, "Fecha de envío al organismo"),
        new(Cierre, "Fecha de cierre"),
        new(Actualizacion, "Última actualización"),
    ];
}

/// <summary>Campos por los que se puede ordenar. Lista cerrada: un campo libre sería inyección de orden.</summary>
public static class CompanyQuerySort
{
    public const string Creado = "creado";
    public const string Enviado = "enviado";
    public const string Cerrado = "cerrado";
    public const string Actualizado = "actualizado";
    public const string Placa = "placa";
    public const string Referencia = "referencia";
    public const string Estado = "estado";
    public const string Organismo = "organismo";
    public const string Tipo = "tipo";
    public const string Compania = "compania";

    public static IReadOnlyList<string> All { get; } =
        [Creado, Enviado, Cerrado, Actualizado, Placa, Referencia, Estado, Organismo, Tipo, Compania];
}

/// <summary>
/// Una fila del resultado: un trámite de la empresa.
///
/// <para><b>Una fila = un trámite, siempre.</b> Filtrar por comprador o por prenda toca tablas
/// hijas, y un join directo multiplicaría la fila del padre por cada hija que coincida: un trámite
/// con dos actores saldría dos veces y todos los totales quedarían inflados. Por eso las
/// condiciones sobre hijas se resuelven como «existe», nunca como cruce.</para>
/// </summary>
public sealed record CompanyQueryRowDto(
    Guid ProcedureInstanceId,
    string ReferenceNumber,
    string? Placa,
    string? Vin,
    Guid? TransitOfficeId,
    string? TransitOfficeName,
    /// <summary>
    /// La compañía dueña del trámite. Se llena siempre, pero solo importa cuando la consulta corre
    /// sobre varias compañías a la vez (SuperAdmin): en el resto de casos es la del propio tenant.
    /// </summary>
    Guid CompaniaId,
    string CompaniaNombre,
    Guid ProcedureTypeId,
    string ProcedureTypeName,
    string Modalidad,
    string Status,
    bool Prioritario,
    bool SubsanacionActiva,
    int SubsanacionCount,
    string? Comprador,
    string? Vendedor,
    bool TienePrenda,
    string? AcreedorPrenda,
    bool TieneLicenciaTransito,
    IReadOnlyList<string> Transformaciones,
    bool EsLeasing,
    string? MetodoPago,
    string TipoTraspaso,
    string RadicadoPor,
    DateTimeOffset CreadoEn,
    DateTimeOffset? EnviadoEn,
    DateTimeOffset? CerradoEn,
    DateTimeOffset? ActualizadoEn,
    double? DiasHastaEnvio,
    double? DiasEnOrganismo,
    int Devoluciones);

/// <summary>
/// El resultado de una consulta de empresa.
///
/// <para><see cref="Total"/> es el universo completo, no las filas devueltas: el contador de la
/// cabecera nunca describe solo la página visible, y el export recorre todas las páginas.</para>
/// </summary>
public sealed record CompanyQueryResultDto(
    int Total,
    int Page,
    int PageSize,
    DateOnly Desde,
    DateOnly Hasta,
    int TotalPeriodoAnterior,
    IReadOnlyList<CompanyQueryRowDto> Filas,
    IReadOnlyList<QueryCoverageItemDto> Cobertura);
