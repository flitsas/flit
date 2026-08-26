namespace Flit.Modules.Quipux.Domain.LogQx;

/// <summary>
/// Estados de la bandeja del LOG QX (HU #11786). NO espejan uno a uno la columna
/// <c>quipux_submissions.status</c>: son estados de PRESENTACIÓN, derivados en la consulta.
/// </summary>
/// <remarks>
/// Dos derivaciones respecto del dato crudo:
/// <list type="bullet">
///   <item><see cref="SinRadicar"/> no existe como radicación — es un trámite ELEGIBLE que todavía
///   no se encoló. Es el caso más caro para soporte y hoy es invisible, porque sin fila en
///   <c>quipux_submissions</c> no había nada que listar.</item>
///   <item><c>registrado</c> se parte en <see cref="Radicado"/> (aún sin sondear) y
///   <see cref="EnTramite"/> (ya se está sondeando). Son situaciones distintas: la primera espera al
///   worker, la segunda espera a la secretaría.</item>
/// </list>
/// El resto (<c>pendiente</c>, <c>aprobado</c>, <c>rechazado</c>, <c>fallido</c>) pasa verbatim.
/// Los contadores se calculan sobre ESTOS estados, para que la suma cuadre con lo que se ve.
/// </remarks>
public static class QuipuxBandejaEstados
{
    public const string SinRadicar = "sin_radicar";
    public const string Pendiente = "pendiente";
    public const string Radicado = "radicado";
    public const string EnTramite = "en_tramite";
    public const string Aprobado = "aprobado";
    public const string Rechazado = "rechazado";
    public const string Fallido = "fallido";

    /// <summary>Orden de presentación de los contadores; también valida el filtro por estado.</summary>
    public static readonly IReadOnlyList<string> Todos =
    [
        SinRadicar, Pendiente, Radicado, EnTramite, Aprobado, Rechazado, Fallido,
    ];

    /// <summary>
    /// Estados NO terminales: el trámite sigue esperando algo y por eso acumula antigüedad. En los
    /// terminales la antigüedad no se calcula (la espera terminó) y la columna va vacía.
    /// </summary>
    public static readonly IReadOnlyList<string> NoTerminales =
    [
        SinRadicar, Pendiente, Radicado, EnTramite,
    ];

    public static bool EsValido(string? estado) =>
        estado is not null && Todos.Contains(estado, StringComparer.Ordinal);
}

/// <summary>
/// Filtro de la bandeja (HU #11786). A diferencia de <see cref="QuipuxLogQuery"/>, cuyos tres ejes
/// eran excluyentes, aquí TODOS los filtros son combinables: se aplican en conjunción. Cualquiera
/// en <c>null</c> simplemente no acota.
/// </summary>
/// <remarks>
/// <see cref="Desde"/> / <see cref="Hasta"/> acotan por la última actividad del trámite, no por su
/// creación: soporte pregunta «qué se movió esta semana», no «qué se creó esta semana». El handler
/// aplica el periodo por defecto cuando no vienen.
/// </remarks>
/// <summary>
/// Familias admitidas en el filtro de la bandeja.
/// </summary>
/// <remarks>
/// Son las tres de <c>procedure_types.family</c> (ADR-0050). Se validan aquí, en el dominio, y no en
/// el borde HTTP porque el criterio —qué es una familia— es del dominio; el borde solo transporta.
/// </remarks>
public static class QuipuxFamilias
{
    public static readonly IReadOnlyList<string> Validas = ["MATRICULAS", "TRASPASO", "OTROS"];

    /// <summary>Devuelve la familia en su forma canónica, o <c>null</c> si no pertenece al dominio.</summary>
    public static string? Normalizar(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        var code = valor.Trim().ToUpperInvariant();
        return Validas.Contains(code) ? code : null;
    }
}

public sealed record QuipuxBandejaQuery(
    DateTimeOffset? Desde,
    DateTimeOffset? Hasta,
    string? Placa,
    Guid? ProcedureInstanceId,
    string? ReferenceNumber,
    string? DocumentoQx,
    string? Estado,
    Guid? TransitOfficeId,
    Guid? TenantId,
    Guid? ProcedureTypeId,
    // Familia del tipo (MATRICULAS | TRASPASO | OTROS). Convive con ProcedureTypeId porque el
    // desplegable ofrece los dos niveles; si llegan ambos, ambos se aplican en AND y manda el más
    // específico por construcción.
    string? Family,
    int Page,
    int PageSize);

/// <summary>
/// Una fila de la bandeja: UN TRÁMITE, no una radicación (ADR-0051, D1). Cuando el trámite acumuló
/// varios intentos, los campos de radicación son los de la MÁS RECIENTE y <see cref="Intentos"/>
/// dice cuántas hubo; el historial completo vive en la pantalla de trazabilidad.
/// </summary>
public sealed class QuipuxBandejaEntry
{
    public Guid ProcedureInstanceId { get; init; }

    public string ReferenceNumber { get; init; } = string.Empty;

    /// <summary>Placa proyectada de <c>procedure_instance_field_values</c>; null en trámites por VIN.</summary>
    public string? Plate { get; init; }

    public string ProcedureTypeName { get; init; } = string.Empty;

    /// <summary>Estado de presentación; ver <see cref="QuipuxBandejaEstados"/>.</summary>
    public string Estado { get; init; } = string.Empty;

    /// <summary>
    /// Empresa dueña del trámite. El id va aparte del nombre porque la consola es multi-tenant:
    /// abrir el trámite desde aquí exige mandar su tenant (<c>?t=</c> → <c>X-Tenant-Id</c>), y el
    /// de la sesión no sirve — soporte ve trámites de otras empresas.
    /// </summary>
    public Guid ClientTenantId { get; init; }

    public string ClientTenantName { get; init; } = string.Empty;

    public string TransitOfficeName { get; init; } = string.Empty;

    public string? DivipoCode { get; init; }

    /// <summary>
    /// Nombre del documento en Quipux — la llave real de correlación con la secretaría (ADR-0051,
    /// D4). Null mientras el trámite no tenga radicación. Quipux NO emite radicado alguno.
    /// </summary>
    public string? DocumentoQx { get; init; }

    /// <summary>Radicación más reciente; null en <c>sin_radicar</c>.</summary>
    public Guid? SubmissionId { get; init; }

    /// <summary>Cuántas radicaciones acumuló el trámite. 0 en <c>sin_radicar</c>.</summary>
    public int Intentos { get; init; }

    public int Attempts { get; init; }

    public int PollCount { get; init; }

    public int? QxRegisterCode { get; init; }

    public int? QxProcedureCode { get; init; }

    public string? RejectionReason { get; init; }

    /// <summary>
    /// Lo último que le pasó al trámite: el evento más reciente de su radicación, o en su defecto la
    /// última escritura de la radicación. Null en <c>sin_radicar</c> (nunca pasó nada).
    /// </summary>
    public DateTimeOffset? UltimaActividad { get; init; }

    /// <summary>
    /// Desde cuándo espera. En <c>sin_radicar</c> es la entrada a <c>preparado</c>; en los demás no
    /// terminales, la creación de la radicación. <b>Null en los terminales</b>: la espera acabó.
    /// </summary>
    public DateTimeOffset? EsperandoDesde { get; init; }

    public DateTimeOffset? SubmissionCreatedAt { get; init; }
}

/// <summary>Contador de un estado sobre el conjunto FILTRADO COMPLETO, no sobre la página.</summary>
public sealed record QuipuxBandejaContador(string Estado, int Total);

/// <summary>Página de la bandeja, con el total y los contadores por estado.</summary>
public sealed record QuipuxBandejaPage(
    IReadOnlyList<QuipuxBandejaEntry> Entries,
    int TotalCount,
    IReadOnlyList<QuipuxBandejaContador> Contadores);
