using Flit.Queries.Domain;

namespace Flit.Analytics.Application.IctQueries;

/// <summary>
/// Sobre qué fecha del pre-trámite se aplica el rango.
///
/// <para>Son otras que las del trámite (<c>CompanyQueryDateField</c>) porque ICT vive en un momento
/// anterior: lo que interesa aquí es cuándo se registró y cuándo pasó cada validación, no cuándo se
/// entregó o se cerró — eso todavía no existe mientras el pre-trámite está en este pipeline.</para>
/// </summary>
public static class IctQueryDateField
{
    /// <summary>Cuándo llegó el pre-trámite a ICT. La lectura por defecto.</summary>
    public const string Registro = "registro";

    /// <summary>Cuándo pasó (o falló) la validación de negocio.</summary>
    public const string ValidacionNegocio = "validacion_negocio";

    /// <summary>Cuándo pasó (o falló) la validación externa (RUNT/RNMC/SOAT).</summary>
    public const string ValidacionExterna = "validacion_externa";

    public static IReadOnlyList<QueryFieldOptionDto> Options { get; } =
    [
        new(Registro, "Fecha de registro"),
        new(ValidacionNegocio, "Fecha de validación de negocio"),
        new(ValidacionExterna, "Fecha de validación externa"),
    ];
}

/// <summary>Campos por los que se puede ordenar. Lista cerrada: un campo libre sería inyección de orden.</summary>
public static class IctQuerySort
{
    public const string Registrado = "registrado";
    public const string Radicado = "radicado";
    public const string Placa = "placa";
    public const string Estado = "estado";

    public static IReadOnlyList<string> All { get; } = [Registrado, Radicado, Placa, Estado];
}

/// <summary>
/// Una fila del resultado: un pre-trámite de ICT.
///
/// <para><see cref="Estado"/> es el estado derivado del pipeline (ver
/// <c>IctQueryRepository.ResolveEstado</c>), no el <c>process_status_id</c> crudo: es lo que se le
/// puede mostrar a un usuario sin que tenga que conocer el número interno.</para>
/// </summary>
public sealed record IctQueryRowDto(
    Guid Id,
    long TransactionNumber,
    string? Radicado,
    string? Placa,
    string? Vin,
    Guid TenantId,
    string TenantNombre,
    string? TipoTramite,
    string Estado,
    bool TieneNovedades,
    bool TieneBorrador,
    bool Prioritario,
    string? Secretaria,
    string? ClienteIntegracion,
    string? Comentarios,
    Guid? ProcedureInstanceId,
    DateTimeOffset RegistradoEn,
    DateTimeOffset? ValidacionNegocioEn,
    DateTimeOffset? ValidacionExternaEn);

/// <summary>
/// El resultado de una consulta de ICT.
///
/// <para><see cref="Total"/> es el universo completo, no las filas devueltas: el contador de la
/// cabecera nunca describe solo la página visible.</para>
/// </summary>
public sealed record IctQueryResultDto(
    int Total,
    int Page,
    int PageSize,
    DateOnly Desde,
    DateOnly Hasta,
    int TotalPeriodoAnterior,
    IReadOnlyList<IctQueryRowDto> Filas,
    IReadOnlyList<QueryCoverageItemDto> Cobertura);
